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

    private string _lastPackItemList = "";

    private readonly object _pendingLock = new();
    private readonly List<(long Id, string Json)> _pendingPackItems = new();
    private readonly List<(long NewId, long OwnedId)> _pendingDuplicates = new();


    private const int PackItemCount = 12;

    private const int SpecialCap = 2;

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

        if (path.Contains("store/transaction"))
            return ("application/json; charset=utf-8", NoTransactionBody());

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
        {
            var inv = ClubStore.Get().Inventory;
            var entries = new StringBuilder("[");
            for (int pileId = 1; pileId <= 5; pileId++)
            {
                if (pileId > 1) entries.Append(',');
                entries.Append("{\"key\":" + pileId + ",\"value\":" + inv.Count(c => c.Pile == pileId) + "}");
            }
            entries.Append(']');
            return ("application/json; charset=utf-8", "{\"entries\":" + entries + "}");
        }

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

        if (path.Contains("/club/consumables/"))
        {
            int cCount = int.TryParse(req.QueryString["count"], out int ccl) ? ccl : 500;
            int cOff = int.TryParse(req.QueryString["start"], out int coff) ? coff : 0;
            var cons = ClubStore.Get().Consumables.Skip(cOff).Take(cCount).ToArray();
            long dnow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var dsb = new StringBuilder("[");
            for (int i = 0; i < cons.Length; i++)
            {
                if (i > 0) dsb.Append(',');
                dsb.Append(ConsumableItems.BuildJson(cons[i], dnow));
            }
            dsb.Append(']');
            return ("application/json; charset=utf-8", "{\"itemData\":" + dsb + "}");
        }

        if (path.EndsWith("/club"))
        {
            int countLimit = int.TryParse(req.QueryString["count"], out int cl) ? cl : 50;
            int offset = int.TryParse(req.QueryString["start"], out int off) ? off : 0;

            string typeFilter = (req.QueryString["type"] ?? "players").ToLowerInvariant();
            if (typeFilter == "equippables")
            {
                var cosmetics = ClubStore.Get().Cosmetics.Skip(offset).Take(countLimit).ToArray();
                long cnow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var csb = new StringBuilder("[");
                for (int i = 0; i < cosmetics.Length; i++)
                {
                    if (i > 0) csb.Append(',');
                    csb.Append(ClubItems.BuildJson(cosmetics[i], cnow));
                }
                csb.Append(']');
                return ("application/json; charset=utf-8", "{\"itemData\":" + csb + "}");
            }
            if (typeFilter == "staff")
            {
                var staff = ClubStore.Get().Staff.Skip(offset).Take(countLimit).ToArray();
                long snow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var ssb = new StringBuilder("[");
                for (int i = 0; i < staff.Length; i++)
                {
                    if (i > 0) ssb.Append(',');
                    ssb.Append(StaffItems.BuildJson(staff[i], snow));
                }
                ssb.Append(']');
                return ("application/json; charset=utf-8", "{\"itemData\":" + ssb + "}");
            }

            string posFilter = req.QueryString["position"] ?? "any";
            int nationFilter = int.TryParse(req.QueryString["nation"], out int nf) ? nf : -1;
            int teamFilter = int.TryParse(req.QueryString["team"], out int tf) ? tf : -1;

            var inventory = ClubStore.Get().Inventory;
            var matches = inventory
                .Where(c => (posFilter == "any" || posFilter == "" || c.Player.Position == posFilter)
                    && (nationFilter == -1 || c.Player.NationId == nationFilter)
                    && (teamFilter == -1 || c.Player.TeamId == teamFilter))
                .DistinctBy(c => c.ItemId)
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

            var tmPool = SpecialCards.All.Concat(RealPlayers.All).ToArray();
            var tmListings = tmPool.Skip(tmStart % tmPool.Length).Take(tmCount).ToArray();
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

        if (path.Contains("/delete/") && path.EndsWith("/item") && req.HttpMethod == "POST")
        {
            var sold = new List<long>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(req.Body);
                if (doc.RootElement.TryGetProperty("itemId", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var el in arr.EnumerateArray()) sold.Add(el.GetInt64());
            }
            catch (Exception ex)
            {
                _log.LogWarning("[FUT] discard body parse failed: {0}", ex.Message);
            }

            long earned = 0;
            ClubStore.Mutate(data =>
            {
                foreach (long id in sold)
                {
                    int idx = data.Inventory.FindIndex(c => c.ItemId == id);
                    if (idx < 0) continue;
                    earned += data.Inventory[idx].Player.Rating * 4;
                    data.Inventory.RemoveAt(idx);
                }
            });
            long balance = 0;
            FutProfileStore.Mutate(p => { p.Coins += earned; balance = p.Coins; });
            lock (_pendingLock)
            {
                _pendingPackItems.RemoveAll(p => sold.Contains(p.Id));
                _pendingDuplicates.RemoveAll(d => sold.Contains(d.NewId));
            }
            _log.LogInformation("[FUT] quick sold {0} item(s) for {1} coins; balance {2}",
                sold.Count, earned, balance);

            var soldSb = new StringBuilder("[");
            for (int i = 0; i < sold.Count; i++)
            {
                if (i > 0) soldSb.Append(',');
                soldSb.Append("{\"id\":" + sold[i] + "}");
            }
            soldSb.Append(']');
            return ("application/json; charset=utf-8",
                    "{\"totalCredits\":" + balance + ",\"items\":" + soldSb + "}");
        }

        if (path.EndsWith("/item") && req.HttpMethod == "PUT")
        {
            var moves = new List<(long Id, string Pile)>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(req.Body);
                if (doc.RootElement.TryGetProperty("itemData", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (!el.TryGetProperty("id", out var idEl)) continue;
                        string pileName = el.TryGetProperty("pile", out var pEl)
                            && pEl.ValueKind == System.Text.Json.JsonValueKind.String
                            ? pEl.GetString() ?? "club" : "club";
                        moves.Add((idEl.GetInt64(), pileName));
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("[FUT] item PUT body parse failed: {0}", ex.Message);
            }

            if (moves.Count > 0)
            {
                ClubStore.Mutate(data =>
                {
                    foreach (var (id, pileName) in moves)
                    {
                        int want = pileName switch { "club" => 6, "trade" => 3, _ => 0 };
                        if (want == 0) continue;
                        int idx = data.Inventory.FindIndex(c => c.ItemId == id);
                        if (idx >= 0 && data.Inventory[idx].Pile != want)
                            data.Inventory[idx] = new ClubItem(id, data.Inventory[idx].Player, want);
                    }
                });
                int left;
                lock (_pendingLock)
                {
                    var claimedIds = new HashSet<long>(moves.Select(m => m.Id));
                    _pendingPackItems.RemoveAll(p => claimedIds.Contains(p.Id));
                    left = _pendingPackItems.Count;
                }
                _log.LogInformation("[FUT] claimed {0} item(s) -> {1}; {2} left to deal with",
                    moves.Count, moves[0].Pile, left);
            }

            var claimed = new StringBuilder("[");
            for (int i = 0; i < moves.Count; i++)
            {
                if (i > 0) claimed.Append(',');
                claimed.Append("{\"id\":" + moves[i].Id + ",\"pile\":\"" + Esc(moves[i].Pile) +
                               "\",\"success\":true}");
            }
            claimed.Append(']');
            return ("application/json; charset=utf-8", "{\"itemData\":" + claimed + "}");
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

                int packPrice = StorePacks.FirstOrDefault(p => p.Id == packId).Coins;
                long coinsAfter = 0;
                FutProfileStore.Mutate(p =>
                {
                    p.Coins = Math.Max(0, p.Coins - packPrice);
                    coinsAfter = p.Coins;
                });
                _log.LogInformation("[FUT] pack {0} opened for {1} coins; balance {2}", packId, packPrice, coinsAfter);

                var drawn = new List<(long Id, string Json)>();
                var dupes = new List<(long NewId, long OwnedId)>();
                var packSpec = StorePacks.FirstOrDefault(p => p.Id == packId);
                string specialSet = packSpec.SpecialSet ?? "";

                double luck = rnd.NextDouble();
                bool hot = luck >= 0.96;
                double ratingBias = luck < 0.80 ? 0.72 : luck < 0.96 ? 0.82 : 0.94;
                double luckMult = luck < 0.80 ? 1.0 : luck < 0.96 ? 1.8 : 3.0;
                var plan = BuildPackPlan(packId, rnd, luckMult);
                if (hot) _log.LogInformation("[FUT] hot pack roll ({0:0.00})", luck);
                ClubStore.Mutate(data =>
                {
                    var used = new HashSet<int>(data.Inventory.Select(c => c.Player.CardId));
                    var ownedByCard = new Dictionary<int, long>();
                    foreach (var c in data.Inventory)
                        if (!ownedByCard.ContainsKey(c.Player.CardId))
                            ownedByCard[c.Player.CardId] = c.ItemId;

                    var picks = new List<(long Id, RealPlayer P, int Tier, bool Rare, bool Special)>();
                    // Cards already dealt in THIS pack. The club holds the whole roster, so the old
                    // "not already owned" filter matched nothing and always fell through to the
                    // unfiltered band - which is how the same player could appear twice in one pack.
                    var packDrawn = new HashSet<int>();
                    int specialsDrawn = 0;
                    for (int i = 0; i < plan.Count; i++)
                    {
                        long itemId = idBase + i;
                        var (tier, wantRare, forceSpecial) = plan[i];
                        RealPlayer[] band = AboveFloor(PackPools[(tier, wantRare)], packSpec.MinRating);
                        if (band.Length == 0) band = PackPools[(tier, false)];
                        if (band.Length == 0) band = RealPlayers.All;
                        // Specials only land on a rare slot, and only from this slot's tier, so a
                        // bronze pack can't hand out a blue card and a gold slot can't hand out a
                        // 63-rated iMOTM. SpecialCap is a hard ceiling per pack whatever the luck
                        // roll says - pulling a promo card should stay an event, and four in one
                        // pack made them worthless.
                        bool takeSpecial = wantRare && specialsDrawn < SpecialCap && forceSpecial;
                        var source = band;
                        bool isSpecial = false;
                        if (takeSpecial
                            && SpecialPools.TryGetValue((specialSet, tier), out var sp) && sp.Length > 0)
                        {
                            source = sp;
                            isSpecial = true;
                        }
                        var pool = source.Where(p => !packDrawn.Contains(p.CardId)).ToArray();
                        if (pool.Length == 0) pool = source;
                        RealPlayer chosen = PickWeighted(pool, rnd, isSpecial ? 0.90 : ratingBias);
                        if (isSpecial) specialsDrawn++;
                        packDrawn.Add(chosen.CardId);
                        picks.Add((itemId, chosen, tier, wantRare, isSpecial));
                    }

                    int topSpecial = picks.Where(x => x.Special).Select(x => x.P.Rating)
                                          .DefaultIfEmpty(0).Max();
                    if (topSpecial > 0)
                        for (int i = 0; i < picks.Count; i++)
                        {
                            var pk = picks[i];
                            if (pk.Special || pk.P.Rating < topSpecial) continue;
                            RealPlayer[] band = AboveFloor(PackPools[(pk.Tier, pk.Rare)], packSpec.MinRating);
                            var lower = band.Where(p => p.Rating < topSpecial && !packDrawn.Contains(p.CardId)).ToArray();
                            if (lower.Length == 0) continue;   // nothing lower in this band, leave it
                            packDrawn.Remove(pk.P.CardId);
                            var swap = PickWeighted(lower, rnd, ratingBias);
                            packDrawn.Add(swap.CardId);
                            picks[i] = (pk.Id, swap, pk.Tier, pk.Rare, false);
                        }

                    foreach (var (itemId, chosen, _, _, _) in picks)
                    {
                        if (ownedByCard.TryGetValue(chosen.CardId, out long ownedId))
                            dupes.Add((itemId, ownedId));
                        else
                            ownedByCard[chosen.CardId] = itemId;
                        drawn.Add((itemId, BuildRealPlayerItem(rnd, chosen, itemId, nowUnix, 1)));
                        data.Inventory.Add(new ClubItem(itemId, chosen, 6));
                    }
                });

                for (int i = 0; i < drawn.Count; i++)
                {
                    if (i > 0) { itemIds.Append(','); items.Append(','); }
                    itemIds.Append(drawn[i].Id);
                    items.Append(drawn[i].Json);
                }
                itemIds.Append(']');
                items.Append(']');
                _lastPackItemList = items.ToString();
                lock (_pendingLock)
                {
                    _pendingPackItems.Clear();
                    _pendingPackItems.AddRange(drawn);
                    _pendingDuplicates.Clear();
                    _pendingDuplicates.AddRange(dupes);
                }
                if (dupes.Count > 0)
                    _log.LogInformation("[FUT] {0} of {1} cards are duplicates", dupes.Count, drawn.Count);

                string purchasedBody = "{\"duplicateItemIdList\":" + DuplicateListJson(dupes) +
                    ",\"itemIdList\":" + itemIds +
                    ",\"itemList\":" + items + ",\"numberItems\":" + drawn.Count +
                    ",\"purchasedPackId\":" + packId + "," +
                    "\"entitlementQuantities\":null,\"awardSetIds\":[]" +
                    ",\"coins\":" + coinsAfter + ",\"credits\":" + coinsAfter +
                    ",\"currencies\":" + CurrenciesJson(coinsAfter) + "}";
                return ("application/json; charset=utf-8", purchasedBody);
            }
            var pending = new StringBuilder("[");
            string pendingDupes;
            lock (_pendingLock)
            {
                for (int i = 0; i < _pendingPackItems.Count; i++)
                {
                    if (i > 0) pending.Append(',');
                    pending.Append(_pendingPackItems[i].Json);
                }
                pendingDupes = DuplicateListJson(
                    _pendingDuplicates.Where(d => _pendingPackItems.Any(p => p.Id == d.NewId)));
            }
            pending.Append(']');
            return ("application/json; charset=utf-8",
                    "{\"duplicateItemIdList\":" + pendingDupes + ",\"itemData\":" + pending + "}");
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
            }

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

    private static readonly (int Id, string Group, int Coins, bool Premium, int Art,
                             int Gold, int Silver, int Bronze, int Rare, int Special,
                             int SpecialMin, int MinRating, string SpecialSet)[] StorePacks =
    {
        // id     tab        coins  prem  art  gold silv bron  rare  spc  min  floor  set
        (100, "bronze",     400, false,   1,   0,   0,  12,   1,   0,   0,   0, ""),  // Bronze Pack
        (103, "bronze",     750, true,    1,   0,   0,  12,   3,   0,   0,   0, ""),  // Premium Bronze Pack
        (200, "silver",    2500, false,   2,   0,  12,   0,   1,   0,   0,   0, ""),  // Silver Pack
        (203, "silver",    3750, true,    2,   0,  12,   0,   3,   0,   0,   0, ""),  // Premium Silver Pack
        (300, "gold",      5000, false,   3,  12,   0,   0,   1,   0,   0,   0, ""),  // Gold Pack
        (304, "gold",      7500, true,    3,  12,   0,   0,   3,   3,   0,   0, ""),  // Premium Gold Pack
        (502, "gold",     15000, true,    3,  12,   0,   0,   3,   8,   0,   0, ""),  // Premium Gold Players Pack
        (405, "special",  35000, true,    4,  12,   0,   0,  12,   8,   0,   0, ""),  // Rare Players Pack - 12 rare gold
        (406, "special",  50000, true,    5,  24,   0,   0,  24,  25,   0,  79, ""),  // Jumbo Rare Players - 24 rare gold
        (404, "special", 100000, true,    6,  30,   0,   0,  30,  45,   0,  82, ""),  // Mega Pack - 30 items
    };

    private static int TierOf(RealPlayer p) => p.Rating >= 75 ? 2 : p.Rating >= 65 ? 1 : 0;

    private static readonly Dictionary<(int Tier, bool Rare), RealPlayer[]> PackPools = BuildPackPools();

    private static readonly Dictionary<int, RealPlayer[]> TierPools =
        Enumerable.Range(0, 3).ToDictionary(t => t,
            t => RealPlayers.All.Where(p => TierOf(p) == t).OrderByDescending(p => p.Rating).ToArray());

    private static readonly Dictionary<(string Set, int Tier), RealPlayer[]> SpecialPools = BuildSpecialPools();

    private static Dictionary<(string, int), RealPlayer[]> BuildSpecialPools()
    {
        var pools = new Dictionary<(string, int), RealPlayer[]>();
        var sets = SpecialCards.All.Select(p => p.Set ?? "").Append("").Distinct();
        foreach (string set in sets)
            for (int tier = 0; tier <= 2; tier++)
                pools[(set, tier)] = SpecialCards.All
                    .Where(p => TierOf(p) == tier
                        && (set.Length == 0 || string.Equals(p.Set, set, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
        return pools;
    }

    private static Dictionary<(int, bool), RealPlayer[]> BuildPackPools()
    {
        var pools = new Dictionary<(int, bool), RealPlayer[]>();
        for (int tier = 0; tier <= 2; tier++)
            foreach (bool rare in new[] { false, true })
                pools[(tier, rare)] = RealPlayers.All
                    .Where(p => TierOf(p) == tier && (p.Rare != 0) == rare)
                    .OrderByDescending(p => p.Rating)   // index 0 = best; PickBiased leans on this
                    .ToArray();
        return pools;
    }

    private static RealPlayer PickWeighted(RealPlayer[] pool, Random rnd, double decay)
    {
        if (pool.Length <= 1) return pool[0];
        int floor = pool[^1].Rating;          // pool is sorted best-first
        double total = 0;
        for (int i = 0; i < pool.Length; )
        {
            int rating = pool[i].Rating, n = 0;
            while (i + n < pool.Length && pool[i + n].Rating == rating) n++;
            total += n * Math.Pow(decay, rating - floor);
            i += n;
        }
        double roll = rnd.NextDouble() * total;
        for (int i = 0; i < pool.Length; )
        {
            int rating = pool[i].Rating, n = 0;
            while (i + n < pool.Length && pool[i + n].Rating == rating) n++;
            roll -= n * Math.Pow(decay, rating - floor);
            if (roll <= 0) return pool[rnd.Next(i, i + n)];
            i += n;
        }
        return pool[^1];
    }

    private static RealPlayer[] AboveFloor(RealPlayer[] band, int minRating)
    {
        if (minRating <= 0 || band.Length == 0 || band[^1].Rating >= minRating) return band;
        int n = 0;
        while (n < band.Length && band[n].Rating >= minRating) n++;
        return n >= 8 ? band[..n] : band;   // never squeeze the pool down to nothing
    }

    private static List<(int Tier, bool Rare, bool Special)> BuildPackPlan(int packId, Random rnd, double luckMult)
    {
        var spec = StorePacks.FirstOrDefault(p => p.Id == packId);
        if (spec.Id == 0) spec = (packId, "bronze", 0, false, 1, 0, 0, PackItemCount, 1, 0, 0, 0, "");

        var plan = new List<(int Tier, bool Rare, bool Special)>();
        for (int i = 0; i < spec.Gold; i++) plan.Add((2, false, false));
        for (int i = 0; i < spec.Silver; i++) plan.Add((1, false, false));
        for (int i = 0; i < spec.Bronze; i++) plan.Add((0, false, false));
        if (plan.Count == 0)
            for (int i = 0; i < PackItemCount; i++) plan.Add((0, false, false));

        for (int i = 0; i < Math.Min(spec.Rare, plan.Count); i++)
            plan[i] = (plan[i].Tier, true, false);
        int specials = Math.Min(spec.SpecialMin, SpecialCap);
        if (specials < SpecialCap && spec.Special > 0 && rnd.NextDouble() * 100 < spec.Special * luckMult)
            specials++;
        for (int i = 0; i < Math.Min(specials, Math.Min(spec.Rare, plan.Count)); i++)
            plan[i] = (plan[i].Tier, true, true);

        for (int i = plan.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (plan[i], plan[j]) = (plan[j], plan[i]);
        }
        return plan;
    }

    private static string DuplicateListJson(IEnumerable<(long NewId, long OwnedId)> dupes)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var (newId, ownedId) in dupes)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"duplicateItemId\":" + newId + ",\"itemId\":" + ownedId + "}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string NoTransactionBody() =>
        "{\"transactionId\":0,\"state\":\"NOTRANSACTION\",\"packId\":0,\"purchasePackType\":\"\"," +
        "\"firstPartyStoreId\":0,\"useAuth\":0,\"useCount\":0,\"useTime\":0}";

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
            int gold   = p.Gold;
            int silver = p.Silver;
            int bronze = p.Bronze;
            int rare   = p.Rare;
            int items  = gold + silver + bronze;
            int art    = p.Art;
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
              .Append(",\"packContentInfo\":{\"itemQuantity\":").Append(items).Append(",\"goldQuantity\":").Append(gold)
              .Append(",\"silverQuantity\":").Append(silver).Append(",\"bronzeQuantity\":").Append(bronze)
              .Append(",\"rareQuantity\":").Append(rare).Append("}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string BuildRealPlayerItem(Random rnd, RealPlayer player, long id, long timestamp, int pile)
    {
        int rating = player.Rating;
        int assetId = player.Id;
        int resourceId = player.CardId;
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
            "\"untradeable\":false,\"assetId\":" + assetId + ",\"rating\":" + rating + "," +
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
