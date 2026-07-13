using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FIFAServer14;

internal sealed class WebServer
{
    private readonly ILogger _log;
    private readonly HttpListener _listener = new();
    private readonly int _port;
    private readonly string _contentRoot;

    public WebServer(int port, ILogger log)
    {
        _port = port;
        _log = log;
        _contentRoot = FindContentRoot();
        _log.LogInformation("OSDK web content root: {0}", _contentRoot);
        // Loopback + a literal IP prefix binds without an admin urlacl reservation.
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
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
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            _log.LogError("WebServer failed to bind :{Port} ({Error}). If access-denied, run once as admin:\n" +
                          $"  netsh http add urlacl url=http://127.0.0.1:{_port}/ user=Everyone", _port, ex.Message);
            return;
        }

        _log.LogInformation("OSDK web listener up on http://127.0.0.1:{0}/", _port);

        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            _ = HandleAsync(ctx);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        try
        {
            string body = "";
            if (req.HasEntityBody)
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                body = await reader.ReadToEndAsync();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[WEB] {req.HttpMethod} {req.RawUrl}");
            foreach (string h in req.Headers)
                sb.AppendLine($"        {h}: {req.Headers[h]}");
            if (body.Length > 0)
                sb.AppendLine($"      body({body.Length}): {Trim(body, 2048)}");
            _log.LogInformation(sb.ToString().TrimEnd());

            var (contentType, payloadStr) = Route(req);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = contentType;

            string lp = (req.Url?.AbsolutePath ?? "").ToLowerInvariant();
            if (lp.Contains("/rs4") || lp.StartsWith("/fut") || lp.Contains("accountinfo") || lp.Contains("/ut/"))
            {
                long nucleusId = ParseLong(req.Headers["Easw-Session-Data-Nucleus-Id"], BlazePersonaId);
                ctx.Response.Headers["sid"] = SessionId;
                ctx.Response.Headers["EASW-Session"] = SessionId;
                ctx.Response.Headers["EASW-Token"] = SessionId;
                ctx.Response.Headers["EASW-Userid"] = nucleusId.ToString();
                ctx.Response.Headers["X-UT-SID"] = SessionId;
                ctx.Response.Headers["X-POW-SID"] = SessionId;
            }

            if (lp.Contains("/pow/"))
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-HTTP-Method-Override";
                ctx.Response.Headers["X-Pow-Sid"] = PowSid;
            }

            var payload = Encoding.UTF8.GetBytes(payloadStr);

            if (lp.Contains("/pow/") && payload.Length > 0 &&
                (req.Headers["Accept-Encoding"] ?? "").Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream();
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                    gz.Write(payload, 0, payload.Length);
                ctx.Response.Headers["X-Unzippedlength"] = payload.Length.ToString();
                ctx.Response.Headers["Content-Encoding"] = "gzip";
                payload = ms.ToArray();
            }

            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            if (payloadStr.Length > 0)
                _log.LogInformation("      -> {0} resp({1}): {2}", contentType, payload.Length, Trim(payloadStr, 2048));
        }
        catch (Exception ex)
        {
            _log.LogWarning("WebServer handler error: {0}", ex.Message);
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private (string, string) Route(HttpListenerRequest req)
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

            bool returning = Environment.GetEnvironmentVariable("FIFA14_FUT_RETURNING") == "1";

            string persona;
            if (returning)
            {
                const string Sku = "FFA14PCC";
                string club =
                    "{\"year\":2014,\"teamId\":1,\"teamName\":\"FC Server\",\"clubName\":\"FC Server\"," +
                    "\"clubAbbr\":\"SRV\",\"clubId\":1,\"platform\":\"pc\",\"assetId\":1,\"badgeId\":1," +
                    "\"seasonId\":1,\"status\":1,\"established\":1,\"divisionOnline\":1," +
                    "\"lastAccessTime\":1400000000," +
                    "\"skuAccessList\":{\"" + Sku + "\":1,\"FFA14PS3\":1,\"FFA14XBX\":1}}";
                persona =
                    "{\"personaId\":" + nucleusId + ",\"personaName\":\"" + BlazePersonaName + "\"," +
                    "\"returningUser\":true,\"trial\":false,\"userState\":\"\"," +
                    "\"userClubList\":[" + club + "]}";
            }
            else
            {
                persona =
                    "{\"personaId\":" + nucleusId + ",\"personaName\":\"" + BlazePersonaName + "\"," +
                    "\"returningUser\":false,\"trial\":false,\"userState\":\"\",\"userClubList\":[]}";
            }
            string json =
                "{\"userAccountInfo\":{\"personas\":[" + persona + "],\"userPersonaInfos\":[]}}";
            return ("application/json; charset=utf-8", json);
        }

        if (path.EndsWith("dimerouting.xml") || path.EndsWith("cfgrouting.xml"))
            return ServeFile("dimerouting.xml", "text/xml; charset=utf-8");

        if (path.EndsWith("futboot.xml"))
            return ServeFile("futBoot.xml", "text/xml; charset=utf-8");

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

    private string PowBody(string path, HttpListenerRequest req)
    {
        if (path.EndsWith("/auth"))
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";
            return $"{{\"lastOnlineTime\":\"{now}\",\"serverTime\":\"{now}\",\"sid\":\"{PowSid}\"}}";
        }
        if (path.Contains("/healthcheck"))                        return "{\"status\":\"ok\"}";
        if (path.Contains("/lvl/weight"))                         return "{\"level\":1,\"xp_per_level\":100}";
        if (path.Contains("/lvl/user"))                           return "{\"level\":1,\"xp\":0,\"tier_gp\":\"businessunit\",\"tier_tp\":\"fifa\"}";
        if (path.Contains("/bank/user/account"))                  return "{\"currency\":\"COINS\",\"balance\":0}";
        if (path.Contains("/bank/currency") && path.Contains("cap/info")) return "{\"currency\":\"pow_funds\",\"cap\":1000000}";
        if (path.Contains("/store/") && path.Contains("catalog"))
            return "{\"catalogs\":[{\"catalogId\":1,\"name\":\"FIFA 14 Store\"}]}";
        if (path.Contains("/inventory/item"))                     return "[]";
        if (path.Contains("/mm/") && path.Contains("message/list"))
            return "{\"messageList\":[],\"messagesAvailable\":0,\"messagesRead\":0,\"promoUpdate\":[]}";
        if (path.Contains("/pfyc/user"))
        {
            long nuc = ParseLong(req.QueryString["friendtiertp"], BlazePersonaId);
            return "{\"users\":[{\"nucId\":" + nuc + ",\"clubId\":1,\"pendingClubId\":0," +
                   "\"numChangesAllowed\":0,\"leagueId\":0,\"globalLeagueId\":0}]}";
        }
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
}
