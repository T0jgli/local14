using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FIFAServer14;

internal sealed class WebServer
{
    private readonly ILogger _log;
    private readonly int _port;
    private readonly string _contentRoot;
    private TcpListener _listener = null!;

    public WebServer(int port, ILogger log)
    {
        _port = port;
        _log = log;
        _contentRoot = FindContentRoot();
        _log.LogInformation("OSDK web content root: {0}", _contentRoot);
    }

    private static string FindContentRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var web = Path.Combine(dir, "web");
            if (Directory.Exists(web))
                return web;
            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }
        return Path.Combine(AppContext.BaseDirectory, "web");
    }

    public async Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        try { _listener.Start(); }
        catch (SocketException ex) { _log.LogError("WebServer failed to bind :{Port} ({Error})", _port, ex.Message); return; }

        _log.LogInformation("OSDK web listener up on http://127.0.0.1:{0}/", _port);

        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { break; }
            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            using var stream = new BufferedStream(client.GetStream(), 16384);
            try
            {
                while (true)
                {
                    WebReq req;
                    try { req = await ReadRequestAsync(stream); }
                    catch { break; }
                    if (req is null) break;   // connection closed

                    bool keepAlive = !string.Equals(req.Headers["Connection"], "close", StringComparison.OrdinalIgnoreCase);
                    byte[] response;
                    try { response = BuildResponse(req, keepAlive); }
                    catch (Exception ex)
                    {
                        _log.LogWarning("WebServer handler error: {0}", ex.Message);
                        response = BuildBytes("500 Internal Server Error", "text/plain", Array.Empty<byte>(), null, false);
                        keepAlive = false;
                    }

                    await stream.WriteAsync(response);
                    await stream.FlushAsync();
                    if (!keepAlive) break;
                }
            }
            catch (Exception ex) { _log.LogWarning("WebServer connection error: {0}", ex.Message); }
        }
    }

    private static async Task<WebReq> ReadRequestAsync(Stream stream)
    {
        var header = new List<byte>(1024);
        var one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1));
            if (n == 0) return null;
            header.Add(one[0]);
            int c = header.Count;
            if (c >= 4 && header[c - 1] == 10 && header[c - 2] == 13 && header[c - 3] == 10 && header[c - 4] == 13) break;
            if (c > 65536) return null;
        }

        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) return null;

        var headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            int idx = lines[i].IndexOf(':');
            if (idx > 0) headers[lines[i][..idx].Trim()] = lines[i][(idx + 1)..].Trim();
        }

        string body = "";
        if (int.TryParse(headers["Content-Length"], out int len) && len > 0)
        {
            var buf = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = await stream.ReadAsync(buf.AsMemory(got, len - got));
                if (n == 0) break;
                got += n;
            }
            body = Encoding.UTF8.GetString(buf, 0, got);
        }

        return new WebReq(parts[0], parts[1], headers, body);
    }

    private byte[] BuildResponse(WebReq req, bool keepAlive)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[WEB] {req.HttpMethod} {req.RawUrl}");
        foreach (string h in req.Headers)
            sb.AppendLine($"        {h}: {req.Headers[h]}");
        if (req.Body.Length > 0)
            sb.AppendLine($"      body({req.Body.Length}): {Trim(req.Body, 2048)}");
        _log.LogInformation(sb.ToString().TrimEnd());

        string lp = (req.Url?.AbsolutePath ?? "").ToLowerInvariant();
        var extra = new NameValueCollection();

        int i2014 = lp.IndexOf("/2014/", StringComparison.Ordinal);
        if (i2014 >= 0)   // serve any real live-content file we have (roster .bin, metadata/fixtures .json, gotw assets)
        {
            string rel = (req.Url?.AbsolutePath ?? "").Substring(i2014 + 6).TrimStart('/');
            string root = Path.GetFullPath(_contentRoot);
            string file = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file))
            {
                var fbytes = File.ReadAllBytes(file);
                string ct = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".json" => "application/json; charset=utf-8",
                    ".xml"  => "text/xml; charset=utf-8",
                    ".png"  => "image/png",
                    _       => "application/octet-stream",
                };
                _log.LogInformation("      -> static {0} ({1} bytes)", rel, fbytes.Length);
                return BuildBytes("200 OK", ct, fbytes, extra, keepAlive);
            }
        }

        var (contentType, payloadStr) = Route(req);

        if (lp.Contains("/rs4") || lp.StartsWith("/fut") || lp.Contains("accountinfo") || lp.Contains("/ut/"))
        {
            long nucleusId = ParseLong(req.Headers["Easw-Session-Data-Nucleus-Id"], BlazePersonaId);
            extra["sid"] = SessionId;
            extra["EASW-Session"] = SessionId;
            extra["EASW-Token"] = SessionId;
            extra["EASW-Userid"] = nucleusId.ToString();
            extra["X-UT-SID"] = SessionId;
            extra["X-POW-SID"] = SessionId;
        }

        if (lp.Contains("/pow/"))
        {
            extra["Access-Control-Allow-Origin"] = "*";
            extra["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            extra["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-HTTP-Method-Override";
            extra["X-Pow-Sid"] = PowSid;
        }

        var payload = Encoding.UTF8.GetBytes(payloadStr);

        if (lp.Contains("/pow/") && payload.Length > 0 &&
            (req.Headers["Accept-Encoding"] ?? "").Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                gz.Write(payload, 0, payload.Length);
            extra["X-Unzippedlength"] = payload.Length.ToString();
            extra["Content-Encoding"] = "gzip";
            payload = ms.ToArray();
        }

        if (payloadStr.Length > 0)
            _log.LogInformation("      -> {0} resp({1}): {2}", contentType, payload.Length, Trim(payloadStr, 2048));

        return BuildBytes("200 OK", contentType, payload, extra, keepAlive);
    }

    private static byte[] BuildBytes(string status, string contentType, byte[] body, NameValueCollection extra, bool keepAlive)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
        sb.Append("Date: ").Append(DateTime.UtcNow.ToString("r")).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        if (extra != null)
            foreach (string k in extra.Keys)
                if (k != null) sb.Append(k).Append(": ").Append(extra[k]).Append("\r\n");
        sb.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[head.Length + body.Length];
        Buffer.BlockCopy(head, 0, result, 0, head.Length);
        Buffer.BlockCopy(body, 0, result, head.Length, body.Length);
        return result;
    }

    // Lightweight stand-in for HttpListenerRequest so the routing code below is unchanged.
    private sealed class WebReq
    {
        public string HttpMethod { get; }
        public string RawUrl { get; }
        public Uri Url { get; }
        public NameValueCollection Headers { get; }
        public NameValueCollection QueryString { get; }
        public string Body { get; }

        public WebReq(string method, string rawUrl, NameValueCollection headers, string body)
        {
            HttpMethod = method;
            RawUrl = rawUrl;
            Headers = headers;
            Body = body ?? "";
            Url = new Uri("http://localhost" + (rawUrl.StartsWith('/') ? rawUrl : "/" + rawUrl), UriKind.Absolute);
            QueryString = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            foreach (var part in Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq >= 0) QueryString[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..]);
                else QueryString[Uri.UnescapeDataString(part)] = "";
            }
        }
    }

    private (string, string) Route(WebReq req)
    {
        string path = (req.Url?.AbsolutePath ?? "").ToLowerInvariant();
        bool wantsJson = (req.Headers["Accept"] ?? "").Contains("json", StringComparison.OrdinalIgnoreCase);

        // FUT/EASFC accountinfo. This is the EASFC backend ("rs4") handshake — the persona
        // here MUST match the one we authenticated over Blaze (AuthenticationComponent:
        // personaId=1000, name="FUT14"), or EASFC can't associate the session and shows
        // "unable to connect". The client sends its id in Easw-Session-Data-Nucleus-Id.
        // Field names confirmed in fifa14.exe: userAccountInfo/personas/personaId/
        // personaName/userClubList. Empty userClubList = new FUT user (no club yet).
        if (path.Contains("/pow/"))
            return ("application/json; charset=utf-8", PowBody(path, req));


        if (path.EndsWith("/accountinfo"))
        {
            long nucleusId = ParseLong(req.Headers["Easw-Session-Data-Nucleus-Id"], BlazePersonaId);
            var prof = FutProfileStore.Get();

            string ret = prof.IsReturningUser ? "true" : "false";
            string clubList = "";
            if (prof.Club.Established)
            {
                const string Sku = "FFA14PCC";
                clubList =
                    "{\"year\":2014,\"teamId\":" + prof.Club.TeamId +
                    ",\"teamName\":\"" + Esc(prof.Club.Name) + "\",\"clubName\":\"" + Esc(prof.Club.Name) + "\"," +
                    "\"clubAbbr\":\"" + Esc(prof.Club.Abbr) + "\",\"clubId\":" + prof.Club.TeamId +
                    ",\"platform\":\"pc\",\"assetId\":" + prof.Club.BadgeId + ",\"badgeId\":" + prof.Club.BadgeId +
                    ",\"seasonId\":1,\"status\":1,\"established\":1,\"divisionOnline\":1,\"lastAccessTime\":1400000000," +
                    "\"skuAccessList\":{\"" + Sku + "\":1,\"FFA14PS3\":1,\"FFA14XBX\":1}}";
            }
            string persona =
                "{\"personaId\":" + nucleusId + ",\"personaName\":\"" + BlazePersonaName + "\"," +
                "\"returningUser\":" + ret + ",\"isReturningUser\":" + ret + ",\"trial\":false,\"userState\":\"\"," +
                "\"userClubList\":[" + clubList + "]}";
            string json =
                "{\"userAccountInfo\":{\"personas\":[" + persona + "],\"userPersonaInfos\":[]}}";
            return ("application/json; charset=utf-8", json);
        }

        if (path.EndsWith("/auth") && (path.Contains("rs4") || path.Contains("/ut")))
            return ("application/json; charset=utf-8", "{\"sid\":\"" + SessionId + "\"}");

        if (path.EndsWith("dimerouting.xml") || path.EndsWith("cfgrouting.xml"))
            return ServeFile("dimerouting.xml", "text/xml; charset=utf-8");

        if (path.EndsWith("futboot.xml"))
            return ServeFile("futBoot.xml", "text/xml; charset=utf-8");

        if (path.EndsWith("/rosterupdate") || path.Contains("rosterupdate.xml"))
            return ServeFile("rosterupdate.xml", "text/xml; charset=utf-8");

        if (path.Contains("dimecfg.xml"))
            return ServeFile("dimecfg.xml", "text/xml; charset=utf-8");

        if (path.Contains("storecfg.xml"))
            return ServeFile("storecfg.xml", "text/xml; charset=utf-8");

        if (path.Contains("storedesc"))
            return ServeFile("storedesc.xml", "text/xml; charset=utf-8");

        if (path.Contains("sponsoredevents") || path.Contains("events_list.xml"))
            return ServeFile("events_list.xml", "text/xml; charset=utf-8");

        if (path.Contains("audiodnplist.csv"))
            return ServeFile("audioDNPList.csv", "text/csv; charset=utf-8");

        if (path.Contains("/trusteddevice"))
            return ("application/json; charset=utf-8",
                    "{\"changed\":false,\"exists\":true,\"locked\":false,\"trusted\":true}");

        if (path.EndsWith("/settings"))
            return ("application/json; charset=utf-8",
                    "{\"configs\":[" +
                    "{\"value\":1,\"type\":\"tokenRedemptionEnabled\"}," +
                    "{\"value\":1,\"type\":\"fifaPointsCancelTransactionFix\"}," +
                    "{\"value\":5,\"type\":\"clubCreateThreshold\"}," +
                    "{\"value\":90,\"type\":\"getOperationTimeoutSec\"}," +
                    "{\"value\":100,\"type\":\"maximumTradePileSize\"}]}");

        if (path.EndsWith("/squad/list"))
            return ("application/json; charset=utf-8",
                    "{\"squad\":[{\"id\":1,\"squadName\":\"FUT14 FC\",\"formation\":\"f442\"," +
                    "\"chemistry\":0,\"rating\":0}]}");

        // FUT user profile (/fut/rs4/ut/game/fifa14/user, .../userdata). Data-driven from the
        // profile: isReturningUser=false => NEW player (client state STATE_WELCOME, not
        // WELCOMEBACK — field name confirmed in fifa14.exe @ 0x1019992c). The parser hashes
        // field names and skips unknown ones, so extra fields are harmless.
        if (path.EndsWith("/user") || path.EndsWith("/userdata"))
        {
            var prof = FutProfileStore.Get();
            return ("application/json; charset=utf-8",
                    "{\"isReturningUser\":" + (prof.IsReturningUser ? "true" : "false") +
                    ",\"established\":" + (prof.Club.Established ? "true" : "false") + "}");
        }

        // Default JSON endpoints
        if (wantsJson || path.StartsWith("/fut"))
            return ("application/json; charset=utf-8", "{}");

        return ("text/xml; charset=utf-8", "");
    }

    private (string, string) ServeFile(string fileName, string contentType)
    {
        var full = Path.Combine(_contentRoot, fileName);
        try
        {
            return (contentType, File.ReadAllText(full));
        }
        catch (Exception ex)
        {
            _log.LogWarning("WebServer missing content file {0}: {1}", full, ex.Message);
            return (contentType, "");
        }
    }

    private string PowBody(string path, WebReq req)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";

        if (path.EndsWith("/auth"))
            return $"{{\"lastOnlineTime\":\"{now}\",\"serverTime\":\"{now}\",\"sid\":\"{PowSid}\"}}";
        if (path.Contains("/healthcheck"))                        return "{\"status\":\"ok\"}";

        if (path.Contains("/lvl/weight"))                         return "{\"level\":1,\"xp_per_level\":100}";
        if (path.Contains("/lvl/user"))
            return "{\"level\":1,\"leveledUp\":false,\"xp\":0,\"xpGained\":0,\"xpLoyalty\":0," +
                   "\"challengesDone\":0,\"xpCapCurrLevel\":0,\"xpCapNextLevel\":100," +
                   "\"funds\":[],\"notifications\":[],\"tier_gp\":\"businessunit\",\"tier_tp\":\"fifa\"}";

        if (path.Contains("/bank/user/account"))                  return "{\"currency\":\"COINS\",\"balance\":" + FutProfileStore.Get().Coins + "}";
        if (path.Contains("/bank/currency") && path.Contains("cap/info")) return "{\"currency\":\"pow_funds\",\"cap\":1000000}";
        if (path.Contains("/bank/"))
            return "{\"currencies\":[{\"currency\":\"pow_funds\",\"funds\":0," +
                   "\"fundsCapInfo\":[{\"period\":\"daily\",\"fundsEarned\":0},{\"period\":\"weekly\",\"fundsEarned\":0}]}]}";

        if (path.Contains("catalog/list"))
            return "{\"catalogs\":[{\"catalogId\":1,\"name\":\"FIFA 14 Store\"}]}";
        if (path.Contains("/store/") && path.Contains("catalog"))
            return "{\"catalogId\":1,\"name\":\"FIFA 14 Store\",\"items\":[]}";
        if (path.Contains("/store/gift"))                         return "{\"gifts\":[]}";
        if (path.Contains("/store/"))                             return "{\"items\":[]}";

        if (path.Contains("/inventory/item"))                     return "[]";

        if (path.Contains("/chal/"))                              return "{\"challenges\":[]}";

        if (path.Contains("/pfyc/") && path.EndsWith("/info"))
            return "{\"clubId\":1,\"clubName\":\"" + BlazePersonaName + "\",\"leagueId\":0," +
                   "\"globalLeagueId\":0,\"division\":1,\"newDivision\":1,\"prevLeagueId\":0}";
        if (path.Contains("/pfyc/schedule"))                      return "{\"schedule\":[]}";
        if (path.Contains("/pfyc/user/club"))
            return "{\"clubId\":1,\"clubName\":\"" + BlazePersonaName + "\",\"leagueId\":0,\"globalLeagueId\":0,\"division\":1}";
        if (path.Contains("/pfyc/user"))
        {
            long nuc = ParseLong(req.QueryString["friendtiertp"], BlazePersonaId);
            return "{\"users\":[{\"nucId\":" + nuc + ",\"clubId\":1,\"pendingClubId\":0," +
                   "\"numChangesAllowed\":0,\"leagueId\":0,\"globalLeagueId\":0}]}";
        }
        if (path.Contains("/pfyc/"))                              return "{}";

        if (path.Contains("/lb/"))                                return "{\"entries\":[]}";

        if (path.Contains("/communication/"))                     return "{\"communications\":[]}";
        if (path.Contains("/mm/") && path.Contains("message/list"))
            return "{\"messageList\":[],\"messagesAvailable\":0,\"messagesRead\":0,\"promoUpdate\":[]}";
        if (path.Contains("/news/"))                              return "{}";

        if (path.Contains("/user/friends"))                       return "{\"friends\":[]}";

        return "{}";
    }

    // Keep these in sync with AuthenticationComponent (UserId / PersonaName) so the EASFC
    // web identity matches the Blaze-authenticated persona.
    private const long BlazePersonaId = 1000;
    private const string BlazePersonaName = "FUT14";

    private const string SessionId = "FIFA14SERVERSESSION0000000000000";

    private const string PowSid = "f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14beef";

    private static long ParseLong(string s, long dflt) => long.TryParse(s, out var v) ? v : dflt;

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "...<truncated>";

    private static string Esc(string s) => (s ?? "")
        .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
