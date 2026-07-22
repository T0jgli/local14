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

    private string _lastPurchaseResponseBody = "";
    private string _lastPackItemList = ""; 

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

        const string dmPrefix = "/fut/dynamicmessages/";
        int idm = lp.IndexOf(dmPrefix, StringComparison.Ordinal);
        if (idm >= 0)
        {
            string rel = (req.Url?.AbsolutePath ?? "").Substring(idm + dmPrefix.Length).TrimStart('/');
            string root = Path.GetFullPath(Path.Combine(_contentRoot, "dynamicmessages"));
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
                _log.LogInformation("      -> dynamicmessages {0} ({1} bytes)", rel, fbytes.Length);
                return BuildBytes("200 OK", ct, fbytes, extra, keepAlive);
            }
            _log.LogWarning("      -> dynamicmessages MISS (no mirror for {0})", rel);
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

        string status = "200 OK";
        if ((lp.EndsWith("/user") || lp.EndsWith("/userdata")) && !FutProfileStore.Get().Club.Established)
            status = "465 Tutorial";

        if (payloadStr.Length > 0)
            _log.LogInformation("      -> [{0}] {1} resp({2}): {3}", status, contentType, payload.Length, Trim(payloadStr, 2048));

        return BuildBytes(status, contentType, payload, extra, keepAlive);
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

        if (path.Contains("purchasegroup"))
            return ("application/json; charset=utf-8", StorePurchaseGroupBody());

        if (path.EndsWith("/accountinfo"))
        {
            long nucleusId = ParseLong(req.Headers["Easw-Session-Data-Nucleus-Id"], BlazePersonaId);
            var prof = FutProfileStore.Get();

            string ret = prof.IsReturningUser ? "true" : "false";
            int est = prof.Club.Established ? 1 : 0;
            const string Sku = "FFA14PCC";
            string clubList =
                "{\"year\":2014,\"teamId\":" + prof.Club.TeamId +
                ",\"teamName\":\"" + Esc(prof.Club.Name) + "\",\"clubName\":\"" + Esc(prof.Club.Name) + "\"," +
                "\"clubAbbr\":\"" + Esc(prof.Club.Abbr) + "\",\"clubId\":" + prof.Club.TeamId +
                ",\"platform\":\"pc\",\"assetId\":" + prof.Club.BadgeId + ",\"badgeId\":" + prof.Club.BadgeId +
                ",\"seasonId\":1,\"status\":" + est + ",\"established\":" + est + ",\"divisionOnline\":1,\"lastAccessTime\":1400000000," +
                "\"skuAccessList\":{\"" + Sku + "\":1,\"FFA14PS3\":1,\"FFA14XBX\":1}}";
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

        if (path.EndsWith("/hub"))
        {
            long coinsHub = FutProfileStore.Get().Coins;
            string currenciesHub = CurrenciesJson(coinsHub);
            return ("application/json; charset=utf-8",
                    "{\"credits\":" + coinsHub + ",\"currencies\":" + currenciesHub +
                    ",\"userInfo\":{\"personaId\":" + BlazePersonaId + ",\"clubName\":\"" + Esc(FutProfileStore.Get().Club.Name) +
                    "\",\"credits\":" + coinsHub + ",\"currencies\":" + currenciesHub +
                    ",\"unassignedPileSize\":0,\"unopenedPacks\":{\"preOrderPacks\":0,\"recoveredPacks\":0}}}");
        }

        if (path.EndsWith("/pilesize"))
            return ("application/json; charset=utf-8", "{}");

        if (path.Contains("/user/credits"))
        {
            long coinsCredits = FutProfileStore.Get().Coins;
            return ("application/json; charset=utf-8",
                    "{\"credits\":" + coinsCredits + ",\"bidTokens\":{},\"currencies\":" + CurrenciesJson(coinsCredits) +
                    ",\"unopenedPacks\":{\"preOrderPacks\":0,\"recoveredPacks\":0},\"futCashBalance\":0}");
        }

        if (path.EndsWith("/tradepile"))
        {
            long coinsTrade = FutProfileStore.Get().Coins;
            return ("application/json; charset=utf-8",
                    "{\"errorState\":null,\"credits\":" + coinsTrade + ",\"auctionInfo\":[],\"currencies\":" + CurrenciesJson(coinsTrade) +
                    ",\"duplicateItemIdList\":[],\"bidTokens\":null,\"maxAuctionsAllowed\":30," +
                    "\"maximumTradePileSize\":100,\"total\":0}");
        }

        if (path.EndsWith("/club"))
        {
            string posFilter = req.QueryString["position"] ?? "any";
            int nationFilter = int.TryParse(req.QueryString["nation"], out int nf) ? nf : -1;
            int teamFilter = int.TryParse(req.QueryString["team"], out int tf) ? tf : -1;
            int countLimit = int.TryParse(req.QueryString["count"], out int cl) ? cl : 50;
            int offset = int.TryParse(req.QueryString["start"], out int off) ? off : 0;

            var inventory = ClubStore.Get().Inventory;
            var matches = inventory
                .Where(c => (posFilter == "any" || posFilter == "" || c.Player.Position == posFilter)
                    && (nationFilter == -1 || c.Player.NationId == nationFilter)
                    && (teamFilter == -1 || c.Player.TeamId == teamFilter))
                .DistinctBy(c => c.Player.Id)
                .OrderByDescending(c => c.Player.Rating)
                .Skip(offset).Take(countLimit)
                .ToArray();

            var clubRnd = new Random();
            long clubNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var itemsSb = new StringBuilder("[");
            for (int i = 0; i < matches.Length; i++)
            {
                if (i > 0) itemsSb.Append(',');
                itemsSb.Append(BuildRealPlayerItem(clubRnd, matches[i].Player, matches[i].ItemId, clubNow, matches[i].Pile));
            }
            itemsSb.Append(']');
            return ("application/json; charset=utf-8", "{\"itemData\":" + itemsSb + "}");
        }

        if (path.EndsWith("/transfermarket"))
        {
            int tmStart = int.TryParse(req.QueryString["start"], out int ts) ? ts : 0;
            int tmCount = int.TryParse(req.QueryString["num"], out int tc) ? tc : 12;

            var tmListings = RealPlayers.All.Skip(tmStart % RealPlayers.All.Length).Take(tmCount).ToArray();
            var tmRnd = new Random();
            long tmNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var auctionSb = new StringBuilder("[");
            for (int i = 0; i < tmListings.Length; i++)
            {
                var p = tmListings[i];
                long tradeId = 700000000L + tmStart + i;
                long itemId = 750000000L + tmStart + i;
                int basePrice = p.Rating * p.Rating * 2;
                int startingBid = Math.Max(150, basePrice / 10);
                int buyNowPrice = Math.Max(startingBid * 3, basePrice);
                string itemJson = BuildRealPlayerItem(tmRnd, p, itemId, tmNow, 6);
                if (i > 0) auctionSb.Append(',');
                auctionSb.Append("{\"tradeId\":" + tradeId + ",\"itemData\":" + itemJson +
                    ",\"startingBid\":" + startingBid + ",\"buyNowPrice\":" + buyNowPrice +
                    ",\"currentBid\":" + startingBid + ",\"expires\":" + (300 + tmRnd.Next(0, 3300)) +
                    ",\"watched\":false,\"bidState\":\"active\",\"tradeState\":\"active\"," +
                    "\"offers\":0,\"tradeOwner\":false}");
            }
            auctionSb.Append(']');
            return ("application/json; charset=utf-8",
                    "{\"auctionInfo\":" + auctionSb + ",\"credits\":" + FutProfileStore.Get().Coins + "}");
        }

        if (path.Contains("/club/stats/newcards"))
        {
            string nc = _lastPackItemList.Length > 0 ? _lastPackItemList : "[]";
            return ("application/json; charset=utf-8", "{\"itemList\":" + nc + "}");
        }

        if (path.Contains("/purchased/items"))
        {
            if (req.HttpMethod == "POST")
            {
                var rnd = new Random();
                long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long idBase = nowUnix % 100000 * 100;
                var itemIds = new StringBuilder("[");
                var items = new StringBuilder("[");

                int packId = 0;
                var packIdMatch = System.Text.RegularExpressions.Regex.Match(req.Body, "\"packId\"\\s*:\\s*(\\d+)");
                if (packIdMatch.Success) int.TryParse(packIdMatch.Groups[1].Value, out packId);
                var currencyMatch = System.Text.RegularExpressions.Regex.Match(req.Body, "\"currency\"\\s*:\\s*\"(\\w+)\"");
                string currency = currencyMatch.Success ? currencyMatch.Groups[1].Value : "";

                ClubStore.Mutate(data =>
                {
                    var used = new HashSet<int>(data.Inventory.Select(c => c.Player.Id));
                    for (int i = 0; i < 12; i++)
                    {
                        long itemId = idBase + i;
                        var pool = RealPlayers.All.Where(p => !used.Contains(p.Id)).ToArray();
                        if (pool.Length == 0) pool = RealPlayers.All;
                        RealPlayer chosen = pool[rnd.Next(pool.Length)];
                        used.Add(chosen.Id);
                        if (i > 0) { itemIds.Append(','); items.Append(','); }
                        itemIds.Append(itemId);
                        items.Append(BuildRealPlayerItem(rnd, chosen, itemId, nowUnix, 6));
                        data.Inventory.Add(new ClubItem(itemId, chosen, 6));
                    }
                });

                itemIds.Append(']');
                items.Append(']');
                _lastPackItemList = items.ToString();
                string purchasedBody = "{\"duplicateItemIdList\":[],\"itemIdList\":" + itemIds +
                    ",\"itemList\":" + items + ",\"numberItems\":12,\"purchasedPackId\":" + packId + "," +
                    "\"entitlementQuantities\":null,\"awardSetIds\":[]}";
                _lastPurchaseResponseBody = purchasedBody;
                return ("application/json; charset=utf-8", purchasedBody);
            }
            string body = _lastPurchaseResponseBody.Length > 0 ? _lastPurchaseResponseBody : "{\"purchase\":[]}";
            return ("application/json; charset=utf-8", body);
        }

        if (path.Contains("/delete/") && System.Text.RegularExpressions.Regex.IsMatch(path, @"squad/(\d+)"))
        {
            int delId = int.Parse(System.Text.RegularExpressions.Regex.Match(path, @"squad/(\d+)").Groups[1].Value);
            ClubStore.Mutate(data =>
            {
                data.Squads.RemoveAll(s => s.Id == delId);
                if (data.ActiveSquadId == delId)
                    data.ActiveSquadId = data.Squads.Count > 0 ? data.Squads[0].Id : 0;
            });
            return ("application/json; charset=utf-8", "{}");
        }

        if (path.EndsWith("/squad") && req.HttpMethod == "POST")
        {
            Squad created = null;
            ClubStore.Mutate(data =>
            {
                int newId = data.Squads.Count > 0 ? data.Squads.Max(s => s.Id) + 1 : 0;
                created = new Squad { Id = newId };
                data.Squads.Add(created);
            });
            return ("application/json; charset=utf-8", BuildFullSquadJson(created));
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"squad/(\d+)") && req.HttpMethod == "PUT")
        {
            int putId = int.Parse(System.Text.RegularExpressions.Regex.Match(path, @"squad/(\d+)").Groups[1].Value);
            Squad target = null;
            ClubStore.Mutate(data =>
            {
                target = data.Squads.FirstOrDefault(s => s.Id == putId);
                if (target == null)
                {
                    target = new Squad { Id = putId };
                    data.Squads.Add(target);
                }
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(req.Body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("players", out var probe) && probe.ValueKind == System.Text.Json.JsonValueKind.Array
                        && probe.GetArrayLength() > 0)
                    {
                        bool anyOwned = false;
                        foreach (var pl in probe.EnumerateArray())
                        {
                            if (!pl.TryGetProperty("itemData", out var it)) continue;
                            if (!it.TryGetProperty("id", out var idp)) continue;
                            long sid = idp.GetInt64();
                            if (sid != 0 && data.Inventory.Any(c => c.ItemId == sid)) { anyOwned = true; break; }
                        }
                        if (!anyOwned) return;  
                    }

                    if (root.TryGetProperty("squadName", out var nameEl) && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        target.Name = nameEl.GetString() ?? target.Name;
                    if (root.TryGetProperty("formation", out var formEl) && formEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        target.Formation = formEl.GetString() ?? target.Formation;
                    if (root.TryGetProperty("chemistry", out var chemEl) && chemEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                        target.Chemistry = chemEl.GetInt32();
                    if (root.TryGetProperty("starRating", out var starEl) && starEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                        target.StarRating = starEl.GetInt32();
                    if (root.TryGetProperty("players", out var playersEl) && playersEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var newSlots = new Dictionary<int, long>();
                        foreach (var pl in playersEl.EnumerateArray())
                        {
                            if (!pl.TryGetProperty("index", out var idxEl)) continue;
                            if (!pl.TryGetProperty("itemData", out var itemDataEl)) continue;
                            if (!itemDataEl.TryGetProperty("id", out var idEl)) continue;
                            long slotItemId = idEl.GetInt64();
                            if (slotItemId != 0 && data.Inventory.Any(c => c.ItemId == slotItemId))
                                newSlots[idxEl.GetInt32()] = slotItemId;
                        }
                        if (newSlots.Count > 0)
                        {
                            target.Slots.Clear();
                            foreach (var kv in newSlots) target.Slots[kv.Key] = kv.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Squad PUT body parse failed: {0}", ex.Message);
                }

                var assigned = new HashSet<long>(data.Squads.SelectMany(s => s.Slots.Values).Where(v => v != 0));
                for (int i = 0; i < data.Inventory.Count; i++)
                {
                    int want = assigned.Contains(data.Inventory[i].ItemId) ? 7 : 6;
                    if (data.Inventory[i].Pile != want)
                        data.Inventory[i] = new ClubItem(data.Inventory[i].ItemId, data.Inventory[i].Player, want);
                }
                data.ActiveSquadId = putId;
            });
            return ("application/json; charset=utf-8", BuildFullSquadJson(target));
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"squad/(\d+)") && req.HttpMethod == "GET")
        {
            int getId = int.Parse(System.Text.RegularExpressions.Regex.Match(path, @"squad/(\d+)").Groups[1].Value);
            var data = ClubStore.Get();
            var target = data.Squads.FirstOrDefault(s => s.Id == getId) ?? new Squad { Id = getId };
            return ("application/json; charset=utf-8", BuildFullSquadJson(target));
        }

        if (path.EndsWith("/squad/active"))
        {
            var data = ClubStore.Get();
            var active = data.Squads.FirstOrDefault(s => s.Id == data.ActiveSquadId)
                ?? (data.Squads.Count > 0 ? data.Squads[0] : new Squad { Id = 0 });
            return ("application/json; charset=utf-8", BuildFullSquadJson(active));
        }

        if (path.EndsWith("/squad/list"))
        {
            var data = ClubStore.Get();
            var sb = new StringBuilder("[");
            for (int i = 0; i < data.Squads.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var s = data.Squads[i];
                sb.Append("{\"id\":" + s.Id + ",\"squadName\":\"" + Esc(s.Name) + "\",\"formation\":\"" + s.Formation +
                    "\",\"chemistry\":" + s.Chemistry + ",\"rating\":" + s.StarRating + "}");
            }
            sb.Append(']');
            return ("application/json; charset=utf-8", "{\"squad\":" + sb + "}");
        }

        // FUT user profile (/fut/rs4/ut/game/fifa14/user, .../userdata). Data-driven from the
        // profile: isReturningUser=false => NEW player (client state STATE_WELCOME, not
        // WELCOMEBACK — field name confirmed in fifa14.exe @ 0x1019992c). The parser hashes
        // field names and skips unknown ones, so extra fields are harmless.
        if (path.EndsWith("/user") || path.EndsWith("/userdata"))
        {
            if (req.HttpMethod == "POST" && req.Body.Contains("clubName"))
            {
                FutProfileStore.Mutate(p =>
                {
                    p.IsReturningUser = true;
                    p.Club.Established = true;
                    var nm = System.Text.RegularExpressions.Regex.Match(req.Body, "\"clubName\"\\s*:\\s*\"([^\"]*)\"");
                    if (nm.Success) p.Club.Name = nm.Groups[1].Value;
                    var ab = System.Text.RegularExpressions.Regex.Match(req.Body, "\"clubAbbr\"\\s*:\\s*\"([^\"]*)\"");
                    if (ab.Success) p.Club.Abbr = ab.Groups[1].Value;
                });
                _log.LogInformation("[FUT] club established: '{0}'", FutProfileStore.Get().Club.Name);
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

    private static readonly (int Id, string Group, string Content, int Coins, bool Premium)[] StorePacks =
    {
        (100, "bronze",  "bronze",   400, false),  // Bronze Pack
        (103, "bronze",  "bronze",   750, true),   // Premium Bronze Pack
        (200, "silver",  "silver",  2500, false),  // Silver Pack
        (203, "silver",  "silver",  3750, true),   // Premium Silver Pack
        (300, "gold",    "gold",    5000, false),  // Gold Pack
        (304, "gold",    "gold",    7500, true),   // Premium Gold Pack
        (502, "gold",    "gold",   15000, true),   // Premium Gold Players Pack
        (405, "special", "gold",   35000, true),   // Rare Players Pack (promo -> Special tab)
    };

    private static string StorePurchaseGroupBody()
    {
        var sb = new StringBuilder();
        sb.Append("{\"id\":\"cardpack\",\"timestamp\":").Append(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        sb.Append(",\"purchase\":[");
        for (int i = 0; i < StorePacks.Length; i++)
        {
            var p = StorePacks[i];
            // Category tabs order left->right by ascending displayGroup.priority: bronze -> special.
            int prio   = p.Group switch { "bronze" => 0, "silver" => 1, "gold" => 2, "special" => 3, _ => 2 };
            // Pack tier (bronze/silver/gold art) is carried by packContentInfo's *Quantity fields.
            int gold   = p.Content == "gold"   ? 10 : 0;
            int silver = p.Content == "silver" ? 10 : 0;
            int bronze = p.Content == "bronze" ? 10 : 0;
            int rare   = p.Premium ? 3 : 1;
            int art    = p.Content == "gold" ? 3 : p.Content == "silver" ? 2 : 1;
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(p.Id)
              .Append(",\"state\":\"active\",\"type\":\"cardpack\",\"description\":\"\"")
              .Append(",\"assetId\":").Append(art).Append(",\"coins\":").Append(p.Coins)
              .Append(",\"actionType\":\"CREATEPACK\",\"productId\":\"0\",\"quantity\":-1")
              .Append(",\"currencies\":[{\"name\":\"COINS\",\"funds\":").Append(p.Coins)
              .Append(",\"finalFunds\":").Append(p.Coins).Append("}]")
              .Append(",\"saleType\":\"NONE\",\"dealType\":\"CARDPACK\",\"saleId\":0")
              .Append(",\"displayGroup\":{\"value\":\"").Append(p.Group).Append("\",\"priority\":").Append(prio).Append('}')
              .Append(",\"sortPriority\":").Append(i)
              .Append(",\"limited\":false,\"purchaseLimit\":0,\"purchaseCount\":0")
              .Append(",\"isPremium\":").Append(p.Premium ? "true" : "false")
              .Append(",\"isSeasonTicketDiscount\":false,\"useDefaultImage\":true")
              .Append(",\"purchaseMethod\":\"COIN\",\"displayGroupAssetId\":").Append(art).Append(",\"lastPurchasedTime\":0")
              .Append(",\"displayGroupUseDefaultImage\":true,\"unopened\":false,\"packType\":\"CARDPACK\"")
              .Append(",\"packContentInfo\":{\"itemQuantity\":12,\"goldQuantity\":").Append(gold)
              .Append(",\"silverQuantity\":").Append(silver).Append(",\"bronzeQuantity\":").Append(bronze)
              .Append(",\"rareQuantity\":").Append(rare).Append("}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string BuildRealPlayerItem(Random rnd, RealPlayer player, long id, long timestamp, int pile)
    {
        int rating = player.Rating;
        int resourceId = player.Id;
        int rareflag = player.Rare;
        int[] attrs = { player.Pace, player.Shooting, player.Passing, player.Dribbling, player.Defending, player.Physical };
        var attrList = new StringBuilder("[");
        for (int a = 0; a < 6; a++)
        {
            if (a > 0) attrList.Append(',');
            attrList.Append("{\"value\":" + attrs[a] + ",\"index\":" + a + "}");
        }
        attrList.Append(']');
        string zeroStats = "[{\"value\":0,\"index\":0},{\"value\":0,\"index\":1},{\"value\":0,\"index\":2},{\"value\":0,\"index\":3},{\"value\":0,\"index\":4}]";
        return "{\"id\":" + id + ",\"timestamp\":" + timestamp + ",\"formation\":\"f442\"," +
            "\"untradeable\":false,\"assetId\":" + resourceId + ",\"rating\":" + rating + "," +
            "\"itemType\":\"player\",\"dream\":false,\"resourceId\":" + resourceId + ",\"owners\":1," +
            "\"discardValue\":" + (rating * 4) + ",\"itemState\":\"free\",\"cardsubtypeid\":3," +
            "\"lastSalePrice\":0,\"morale\":50,\"fitness\":99,\"injuryType\":\"none\",\"injuryGames\":0," +
            "\"preferredPosition\":\"" + player.Position + "\",\"statsList\":" + zeroStats +
            ",\"lifetimeStats\":" + zeroStats + ",\"training\":0,\"contract\":7,\"suspension\":0," +
            "\"marketDataMinPrice\":150,\"marketDataMaxPrice\":15000000,\"attributeList\":" + attrList +
            ",\"teamid\":" + player.TeamId + ",\"rareflag\":" + rareflag + ",\"playStyle\":250," +
            "\"leagueId\":1,\"assists\":0,\"lifetimeAssists\":0," +
            "\"loyaltyBonus\":1,\"pile\":" + pile + ",\"loans\":0,\"nation\":" + player.NationId +
            ",\"resourceGameYear\":2014,\"amount\":0}";
    }

    private static string BuildFullSquadJson(Squad squad)
    {
        var inventory = ClubStore.Get().Inventory;
        var rnd = new Random();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var playersSb = new StringBuilder("[");
        bool first = true;
        long captainId = 0;
        foreach (var slot in squad.Slots)
        {
            if (slot.Value == 0) continue;
            var member = inventory.FirstOrDefault(c => c.ItemId == slot.Value);
            if (member.ItemId == 0) continue;
            string item = BuildRealPlayerItem(rnd, member.Player, member.ItemId, now, 7);
            if (!first) playersSb.Append(',');
            first = false;
            playersSb.Append("{\"index\":" + slot.Key + ",\"loyaltyBonus\":1,\"kitNumber\":0,\"chemistry\":10,\"itemData\":" + item + "}");
            if (captainId == 0 || member.Player.Position == "ST") captainId = member.ItemId;
        }
        playersSb.Append(']');
        // The client computes and PUTs its own chemistry/rating/starRating, so we just
        // persist and echo those back rather than recomputing server-side.
        int rating = squad.StarRating;

        string actives = "[" +
            "{\"id\":800001,\"timestamp\":" + now + ",\"formation\":\"f442\",\"untradeable\":false,\"assetId\":261," +
            "\"rating\":75,\"itemType\":\"stadium\",\"resourceId\":6200057,\"owners\":1,\"discardValue\":110," +
            "\"itemState\":\"activeStadium\",\"cardsubtypeid\":10,\"lastSalePrice\":0,\"statsList\":[]," +
            "\"lifetimeStats\":[],\"attributeList\":[],\"teamid\":0,\"rareflag\":0,\"leagueId\":0,\"pile\":7," +
            "\"resourceGameYear\":2014,\"cardassetid\":36,\"category\":4,\"name\":\"Server Park\"," +
            "\"description\":\"StadiumDesc_Server\",\"biodescription\":\"StadiumDetailDesc\",\"stadiumid\":1,\"capacity\":30000}," +
            "{\"id\":800002,\"timestamp\":" + now + ",\"formation\":\"f442\",\"untradeable\":false,\"assetId\":132," +
            "\"rating\":75,\"itemType\":\"ball\",\"resourceId\":8120223,\"owners\":1,\"discardValue\":110," +
            "\"itemState\":\"activeBall\",\"cardsubtypeid\":30,\"lastSalePrice\":0,\"statsList\":[]," +
            "\"lifetimeStats\":[],\"attributeList\":[],\"teamid\":0,\"rareflag\":0,\"leagueId\":0,\"pile\":7," +
            "\"resourceGameYear\":2014,\"cardassetid\":37,\"category\":1,\"name\":\"Server Ball\",\"value\":75," +
            "\"manufacturer\":\"ManufacturerGeneric\"}," +
            "{\"id\":800003,\"timestamp\":" + now + ",\"formation\":\"f442\",\"untradeable\":false,\"assetId\":14," +
            "\"rating\":75,\"itemType\":\"kit\",\"resourceId\":6300511,\"owners\":1,\"discardValue\":110," +
            "\"itemState\":\"activeHomeKit\",\"cardsubtypeid\":9,\"lastSalePrice\":0,\"statsList\":[]," +
            "\"lifetimeStats\":[],\"attributeList\":[],\"teamid\":1,\"rareflag\":0,\"leagueId\":0,\"pile\":7," +
            "\"resourceGameYear\":2014,\"category\":2,\"year\":0}," +
            "{\"id\":800004,\"timestamp\":" + now + ",\"formation\":\"f442\",\"untradeable\":false,\"assetId\":14," +
            "\"rating\":75,\"itemType\":\"kit\",\"resourceId\":6300719,\"owners\":1,\"discardValue\":55," +
            "\"itemState\":\"activeAwayKit\",\"cardsubtypeid\":9,\"lastSalePrice\":0,\"statsList\":[]," +
            "\"lifetimeStats\":[],\"attributeList\":[],\"teamid\":1,\"rareflag\":0,\"leagueId\":0,\"pile\":7," +
            "\"resourceGameYear\":2014,\"category\":2,\"year\":0}," +
            "{\"id\":800005,\"timestamp\":" + now + ",\"formation\":\"f442\",\"untradeable\":false,\"assetId\":101014," +
            "\"rating\":75,\"itemType\":\"custom\",\"resourceId\":6000170,\"owners\":1,\"discardValue\":110," +
            "\"itemState\":\"activeBadge\",\"cardsubtypeid\":11,\"lastSalePrice\":0,\"statsList\":[]," +
            "\"lifetimeStats\":[],\"attributeList\":[],\"teamid\":1,\"rareflag\":0,\"leagueId\":0,\"pile\":7," +
            "\"resourceGameYear\":2014,\"category\":1,\"value\":75,\"weightrare\":0,\"header\":\"Badge\"}" +
            "]";

        string kicktakers = "[{\"id\":" + captainId + ",\"index\":0},{\"id\":" + captainId + ",\"index\":1}," +
            "{\"id\":" + captainId + ",\"index\":2},{\"id\":" + captainId + ",\"index\":3}," +
            "{\"id\":" + captainId + ",\"index\":4}]";

        return "{\"id\":" + squad.Id + ",\"valid\":true,\"personaId\":" + BlazePersonaId + ",\"formation\":\"" + squad.Formation +
            "\",\"rating\":" + rating + ",\"chemistry\":" + squad.Chemistry +
            ",\"manager\":[{\"id\":0,\"itemType\":\"manager\"}],\"players\":" + playersSb +
            ",\"actives\":" + actives + ",\"dreamSquad\":false,\"changed\":0,\"squadName\":\"" + Esc(squad.Name) + "\"," +
            "\"starRating\":" + rating + ",\"captain\":" + captainId + ",\"kicktakers\":" + kicktakers +
            ",\"squadType\":\"REGULAR_SQUAD\",\"newSquad\":null,\"custom\":null}";
    }

    private static string CurrenciesJson(long coins) =>
        "[{\"name\":\"COINS\",\"funds\":" + coins + ",\"finalFunds\":" + coins + ",\"originalPrice\":" + coins + "}," +
        "{\"name\":\"POINTS\",\"funds\":0,\"finalFunds\":0,\"originalPrice\":0}," +
        "{\"name\":\"DRAFT_TOKEN\",\"funds\":0,\"finalFunds\":0,\"originalPrice\":0}]";

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
