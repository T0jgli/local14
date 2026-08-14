using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Impulsum14;

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

            if (rel.StartsWith("fut/items/pc/", StringComparison.OrdinalIgnoreCase)
                && rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(System.IO.Path.GetFileNameWithoutExtension(rel), out int trophyId)
                && trophyId >= 8200000)
            {
                if (trophyId >= 8202000)
                {
                    string sjson = Seasons.TrophyJson(trophyId - 8202000);
                    _log.LogInformation("      -> season trophy item json {0} entry={1}", rel, trophyId - 8202000);
                    return BuildBytes("200 OK", "application/json; charset=utf-8",
                                      System.Text.Encoding.UTF8.GetBytes(sjson), extra, keepAlive);
                }
                int tourneyId = trophyId - 8200000;   // trophyResourceId 8200000+S -> tournamentId S
                Tournaments.ActiveTournamentId = tourneyId;
                string tjson = Tournaments.TrophyJson(tourneyId);
                _log.LogInformation("      -> trophy item json {0} tournamentId={1} (TOURNY_LOC key)",
                    rel, tourneyId);
                return BuildBytes("200 OK", "application/json; charset=utf-8",
                                  System.Text.Encoding.UTF8.GetBytes(tjson), extra, keepAlive);
            }

            if (rel.StartsWith("fut/items/", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInformation("      -> 404 (CDN-style miss for {0})", rel);
                return BuildBytes("404 Not Found", "text/plain; charset=utf-8",
                                  System.Text.Encoding.UTF8.GetBytes("Not Found"), extra, keepAlive);
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

        if (path.Contains("/match/reset"))
        {
            long balMR = FutProfileStore.Get().Coins;
            return ("application/json; charset=utf-8",
                    "{\"allCoins\":" + balMR + ",\"credits\":" + balMR + ",\"coins\":" + balMR +
                    ",\"currencies\":" + CurrenciesJson(balMR) + "}");
        }
        if (path.Contains("/match/end") || path.Contains("destroymatch"))
        {
            string endReason = BodyRx(req.Body, "\"endReason\"\\s*:\\s*\"([^\"]*)\"");
            bool isWin  = endReason.Equals("WIN",  StringComparison.OrdinalIgnoreCase);
            bool isDraw = endReason.Equals("DRAW", StringComparison.OrdinalIgnoreCase)
                       || endReason.Equals("TIE",  StringComparison.OrdinalIgnoreCase);
            int myGoals = 0;
            var msm = System.Text.RegularExpressions.Regex.Match(req.Body, "\"myMatchStats\"\\s*:\\s*\\{([^}]*)\\}");
            if (msm.Success && int.TryParse(BodyRx(msm.Groups[1].Value, "\"goals\"\\s*:\\s*(\\d+)"), out int g)) myGoals = g;

            int matchCoins = (isWin ? 500 : isDraw ? 300 : 200) + myGoals * 20;

            bool credited = req.HttpMethod is "PUT" or "POST";
            int tournamentCoins = 0;
            int? awardedCup = null;
            long balME = FutProfileStore.Get().Coins;
            if (credited)
            {
                ApplyMatchConsequences();  
                if (isWin && Tournaments.CurrentMatchTournamentId is int tid && Tournaments.CurrentRound >= Tournaments.NumRounds)
                {
                    tournamentCoins = Tournaments.AwardCoins(tid);
                    awardedCup = tid;
                    Tournaments.CurrentMatchTournamentId = null;   // guard against a double-award
                    Tournaments.CurrentRound = 1;
                    Tournaments.ClearProgress(tid);                // cup finished - no longer "underway"
                }
                int total = matchCoins + tournamentCoins;
                bool trophy = tournamentCoins > 0;
                FutProfileStore.Mutate(p => { p.Coins += total; if (trophy) p.TrophiesWon++; balME = p.Coins; });
            }

            if (awardedCup is int wonId)
                _log.LogInformation("[FUT] tournament {0} WON -> +{1} match +{2} cup coins; trophies={3}; balance {4}",
                    wonId, matchCoins, tournamentCoins, FutProfileStore.Get().TrophiesWon, balME);
            else
                _log.LogInformation("[FUT] match/end ({0}, {1} goals): +{2} coins -> {3}",
                    string.IsNullOrEmpty(endReason) ? "?" : endReason, myGoals, credited ? matchCoins : 0, balME);
            return ("application/json; charset=utf-8", MatchEndBody(balME, matchCoins, tournamentCoins));
        }

        if (path.EndsWith("/match"))
        {
            if (req.HttpMethod == "POST")
            {
                string t = BodyRx(req.Body, "\"tournamentId\"\\s*:\\s*(\\d+)");
                Tournaments.CurrentMatchTournamentId = (int.TryParse(t, out int ti) && ti > 0) ? ti : (int?)null;
            }
            return ("application/json; charset=utf-8", "{}");
        }

        if (path.Contains("tournament") || path.EndsWith("/teams"))
        {
            if (path.EndsWith("/teams") || path.Contains("tournamentteams"))
            {
                int gid = int.TryParse(req.QueryString["groupId"], out int g) ? g : 0;
                return ("application/json; charset=utf-8", Tournaments.TeamsJson(gid));
            }

            if (path.Contains("tournament/user/"))
            {
                string tail = path[(path.LastIndexOf('/') + 1)..];
                if (int.TryParse(tail, out int tid))
                {
                    Tournaments.ActiveTournamentId = tid;
                    if (req.HttpMethod is "POST" or "PUT")           // saving bracket progress
                    {
                        int round = int.TryParse(BodyRx(req.Body, "\"round\"\\s*:\\s*(\\d+)"), out int rd) && rd > 0 ? rd : 0;
                        if (round > 0) Tournaments.CurrentRound = round;
                        Tournaments.SaveProgress(tid, round,
                            BodyRx(req.Body, "\"tournamentData\"\\s*:\\s*\"([^\"]*)\""),
                            int.TryParse(BodyRx(req.Body, "\"progressDataVersion\"\\s*:\\s*(\\d+)"), out int pv) ? pv : 0,
                            BodyRx(req.Body, "\"progressData\"\\s*:\\s*\"([^\"]*)\""));
                        return ("application/json; charset=utf-8", "{}");
                    }
                    return ("application/json; charset=utf-8", Tournaments.UserTournamentJson(tid));
                }
                if (req.HttpMethod is "POST" or "PUT")
                    return ("application/json; charset=utf-8", "{}");
                return ("application/json; charset=utf-8", Tournaments.UserListJson());
            }

            if (path.Contains("/schedule"))
                return ("application/json; charset=utf-8", "{\"schedule\":[]}");

            if (req.HttpMethod is "POST" or "PUT")
                return ("application/json; charset=utf-8", "{\"tournament\":[]}");

            if (path.Contains("/delete"))
                return ("application/json; charset=utf-8", "{}");

            return ("application/json; charset=utf-8", Tournaments.CatalogJson());
        }

        if (path.Contains("/season") || path.Contains("/division/"))
        {
            if (path.Contains("/squad/unlock"))
                return ("application/json; charset=utf-8", "{}");

            if (path.Contains("reset"))
            {
                int nd = Seasons.ParseResetDivision(path);
                if (nd >= 0) FutProfileStore.Mutate(p => p.OfflineDivision = nd);
                return ("application/json; charset=utf-8",
                        Seasons.ResetJson(nd >= 0 ? nd : FutProfileStore.Get().OfflineDivision));
            }

            if (path.Contains("season/history"))
                return ("application/json; charset=utf-8", Seasons.HistoryJson());

            if (path.EndsWith("/user") || path.Contains("season/user"))
            {
                if (req.HttpMethod is "PUT" or "POST")
                    FutProfileStore.Mutate(p => Seasons.CaptureSave(p, req.Body));
                return ("application/json; charset=utf-8", Seasons.UserJson(FutProfileStore.Get()));
            }

            // The division ladder catalog (season/list, and any other season GET).
            return ("application/json; charset=utf-8", Seasons.ListJson());
        }

        if (path.EndsWith("/accountinfo"))
        {
            long nucleusId = ParseLong(req.Headers["Easw-Session-Data-Nucleus-Id"], BlazePersonaId);
            return ("application/json; charset=utf-8",
                    "{\"userAccountInfo\":" + UserAccountInfoJson(nucleusId) + "}");
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
            int clubPlayers = ClubStore.Get().Inventory.Count;
            string totwHub = ",\"squad\":" + Totw.HubSquadJson();
            return ("application/json; charset=utf-8",
                    "{\"credits\":" + coinsHub + ",\"currencies\":" + currenciesHub +
                    ",\"userInfo\":{\"personaId\":" + BlazePersonaId + ",\"clubName\":\"" + Esc(FutProfileStore.Get().Club.Name) +
                    "\",\"credits\":" + coinsHub + ",\"currencies\":" + currenciesHub +
                    ",\"unassignedPileSize\":0,\"unopenedPacks\":{\"preOrderPacks\":0,\"recoveredPacks\":0}}" +
                    totwHub +
                    ",\"clubPlayers\":" + clubPlayers +
                    ",\"auctionCount\":0,\"tradePile\":{\"selling\":0,\"sold\":0,\"count\":0,\"notification\":0}" +
                    ",\"watchlist\":{\"winning\":0,\"count\":0,\"outbid\":0,\"notification\":0}}");
        }

        if (path.EndsWith("/clubuser"))
            return ("application/json; charset=utf-8",
                    "{\"user\":[{\"personaId\":" + FutSquadPersonaId + "}," +
                    "{\"personaId\":" + Totw.ClubPersona + ",\"persona\":\"TOTW\",\"public\":true}]}");

        if (path.Contains("/user/list"))
        {
            string q = req.Url?.Query ?? "";
            if (q.Contains(Totw.ClubPersona.ToString()))
            {
                _log.LogInformation("[TOTW] user/list resolved TOTW club (persona {0})", Totw.ClubPersona);
                return ("application/json; charset=utf-8", Totw.ClubInfoJson());
            }
            return ("application/json; charset=utf-8", "{}");
        }

        if (path.EndsWith("/pilesize"))
        {
            var data = ClubStore.Get();
            int clubPlayers = data.Inventory.Count;
            int activeSquad = data.Inventory.Count(c => c.Pile == 7);
            int tradePile   = data.Inventory.Count(c => c.Pile == 3);
            int consumables = AvailableConsumables().Count;   // catalog + owned, matches /club/consumables/
            string entries =
                "[{\"key\":1,\"value\":0},{\"key\":2,\"value\":0},{\"key\":3,\"value\":" + tradePile +
                "},{\"key\":4,\"value\":0},{\"key\":6,\"value\":" + clubPlayers +
                "},{\"key\":7,\"value\":" + activeSquad + "}]";
            string clientData =
                "[{\"pile\":1,\"count\":0,\"maxCount\":100},{\"pile\":2,\"count\":0,\"maxCount\":100}," +
                "{\"pile\":6,\"count\":" + clubPlayers + ",\"maxCount\":2000}," +
                "{\"pile\":7,\"count\":" + activeSquad + ",\"maxCount\":100}]";
            return ("application/json; charset=utf-8",
                "{\"entries\":" + entries + ",\"pileSizeClientData\":" + clientData +
                ",\"clubSize\":" + clubPlayers + ",\"consumableCount\":" + consumables + "}");
        }

        if (path.EndsWith("/clientdata/totw") && req.HttpMethod == "GET")
            return ("application/json; charset=utf-8", Totw.ChallengeEntriesJson());

        if (path.EndsWith("/totw") && !path.Contains("/clientdata/"))
            return ("application/json; charset=utf-8",
                    req.HttpMethod == "GET" ? Totw.SquadChallengeJson() : "{}");

        if (path.Contains("/clientdata/"))
        {
            string key = path[(path.LastIndexOf("/clientdata/", StringComparison.Ordinal) + "/clientdata/".Length)..];
            if (req.HttpMethod == "PUT" || req.HttpMethod == "POST")
            {
                ClientDataStore.Set(key, req.Body);
                return ("application/json; charset=utf-8", "{}");
            }
            return ("application/json; charset=utf-8", ClientDataStore.Get(key));
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

        if (path.EndsWith("/club/stats/consumables"))
            return ("application/json; charset=utf-8", ConsumableStatsJson());

        if (path.Contains("/club/consumables"))
        {
            int cCount = int.TryParse(req.QueryString["count"], out int ccl) ? ccl : 500;
            int cOff = int.TryParse(req.QueryString["start"], out int coff) ? coff : 0;
            string tab = path[(path.LastIndexOf('/') + 1)..].ToLowerInvariant();
            var filter = ConsumableTabFilter(tab);
            var src = filter == null ? AvailableConsumables() : AvailableConsumables().Where(filter).ToList();
            var cons = src.Skip(cOff).Take(cCount).ToArray();
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
            if (path.EndsWith("/user/club") && req.Body.Contains("clubName"))
            {
                string oldName = FutProfileStore.Get().Club.Name;
                FutProfileStore.Mutate(p =>
                {
                    p.Club.Established = true;
                    var nm = System.Text.RegularExpressions.Regex.Match(req.Body, "\"clubName\"\\s*:\\s*\"([^\"]*)\"");
                    if (nm.Success && nm.Groups[1].Value.Length > 0) p.Club.Name = nm.Groups[1].Value;
                    var ab = System.Text.RegularExpressions.Regex.Match(req.Body, "\"clubAbbr\"\\s*:\\s*\"([^\"]*)\"");
                    if (ab.Success && ab.Groups[1].Value.Length > 0) p.Club.Abbr = ab.Groups[1].Value;
                });
                string newName = FutProfileStore.Get().Club.Name;
                ClubStore.Mutate(d =>
                {
                    foreach (var sq in d.Squads)
                        if (string.IsNullOrWhiteSpace(sq.Name) || sq.Name == oldName)
                            sq.Name = newName;
                });
                _log.LogInformation("[FUT] club renamed to '{0}'", newName);
            }

            int countLimit = int.TryParse(req.QueryString["count"], out int cl) ? cl : 50;
            int offset = int.TryParse(req.QueryString["start"], out int off) ? off : 0;

            string typeFilter = (req.QueryString["type"] ?? "players").ToLowerInvariant();
            if (typeFilter is "equippables" or "badge" or "kit" or "ball" or "stadium")
            {
                string cosmeticsLevel = (req.QueryString["level"] ?? "").ToLowerInvariant();
                int cosmeticsLeague = int.TryParse(req.QueryString["league"], out int clg) ? clg : -1;
                int cosmeticsTeam = int.TryParse(req.QueryString["team"], out int ctm) ? ctm : -1;
                var cosmetics = ClubStore.Get().Cosmetics
                    .Where(c => typeFilter == "equippables" || c.Type == typeFilter)
                    .Where(c => cosmeticsLevel switch
                    {
                        "bronze" => c.Rating < 65,
                        "silver" => c.Rating is >= 65 and < 75,
                        "gold" => c.Rating >= 75,
                        _ => true,
                    })
                    .Where(c => cosmeticsLeague == -1 || TeamLeagues.LeagueOf(c.TeamId) == cosmeticsLeague)
                    .Where(c => cosmeticsTeam == -1 || c.TeamId == cosmeticsTeam)
                    .Skip(offset).Take(countLimit).ToArray();
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
            if (typeFilter == "manager")
            {
                int mgrNation = int.TryParse(req.QueryString["nation"], out int mnf) ? mnf : -1;
                int mgrLeague = int.TryParse(req.QueryString["league"], out int mlf) ? mlf : -1;
                string mgrLevel = (req.QueryString["level"] ?? "").ToLowerInvariant();
                long mnow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return ("application/json; charset=utf-8",
                    "{\"itemData\":" + ManagerItemsJson(offset, countLimit, mnow, 6, mgrNation, mgrLeague, mgrLevel) + "}");
            }
            if (typeFilter == "staff" || typeFilter == "headcoach" || typeFilter == "gkcoach"
                || typeFilter == "physio" || typeFilter == "fitnesscoach")
            {
                string staffTypeFilter = typeFilter switch
                {
                    "headcoach" => "headCoach",
                    "gkcoach" => "GKCoach",
                    "fitnesscoach" => "fitnessCoach",
                    "physio" => "physio",
                    _ => null, // "staff": all managers + staff
                };
                string staffLevel = (req.QueryString["level"] ?? "").ToLowerInvariant();
                long snow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return ("application/json; charset=utf-8",
                    "{\"itemData\":" + StaffItemsJson(offset, countLimit, snow, 6, staffTypeFilter, staffLevel) + "}");
            }

            string posFilter = req.QueryString["position"] ?? "any";
            int nationFilter = int.TryParse(req.QueryString["nation"], out int nf) ? nf : -1;
            int teamFilter = int.TryParse(req.QueryString["team"], out int tf) ? tf : -1;
            string levelFilter = (req.QueryString["level"] ?? "").ToLowerInvariant();
            int leagueFilter = int.TryParse(req.QueryString["league"], out int lf) ? lf : -1;

            var inventory = ClubStore.Get().Inventory;
            var matches = inventory
                .Where(c => (posFilter == "any" || posFilter == "" || c.Player.Position == posFilter)
                    && (nationFilter == -1 || c.Player.NationId == nationFilter)
                    && (teamFilter == -1 || c.Player.TeamId == teamFilter)
                    && (leagueFilter == -1 || TeamLeagues.LeagueOf(c.Player.TeamId) == leagueFilter)
                    && levelFilter switch
                    {
                        "bronze" => c.Player.Rating < 65,
                        "silver" => c.Player.Rating is >= 65 and < 75,
                        "gold" => c.Player.Rating >= 75,
                        _ => true,
                    })
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
                long itemId = ItemIds.For(p);
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

        if (path.Contains("/item/resource/") && (req.HttpMethod == "POST" || req.HttpMethod == "PUT"))
        {
            long applyRes = 0;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0) long.TryParse(path[(lastSlash + 1)..], out applyRes);
            var applyTargets = new List<long>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(req.Body);
                if (doc.RootElement.TryGetProperty("apply", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var el in arr.EnumerateArray())
                        if (el.TryGetProperty("id", out var idEl)
                            && idEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            applyTargets.Add(idEl.GetInt64());
            }
            catch (Exception ex) { _log.LogWarning("[FUT] consumable apply body parse failed: {0}", ex.Message); }
            var changedIds = ApplyConsumable(applyRes, applyTargets);
            return ("application/json; charset=utf-8", AppliedItemsJson(applyRes, changedIds));
        }

        if (path.Contains("/delete/") && path.EndsWith("/item") && req.HttpMethod == "POST")
        {
            var sold = new List<long>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(req.Body);
                var root = doc.RootElement;
                if (root.TryGetProperty("itemId", out var itemId))
                {
                    if (itemId.ValueKind == System.Text.Json.JsonValueKind.Array)
                        foreach (var el in itemId.EnumerateArray())
                            if (el.ValueKind == System.Text.Json.JsonValueKind.Number) sold.Add(el.GetInt64());
                    else if (itemId.ValueKind == System.Text.Json.JsonValueKind.Number)
                        sold.Add(itemId.GetInt64());
                }
                else if (root.TryGetProperty("itemIds", out var itemIds)
                         && itemIds.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in itemIds.EnumerateArray())
                        if (el.ValueKind == System.Text.Json.JsonValueKind.Number) sold.Add(el.GetInt64());
                }
                else if (root.TryGetProperty("itemData", out var itemData)
                         && itemData.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in itemData.EnumerateArray())
                        if (item.TryGetProperty("id", out var id)
                            && id.ValueKind == System.Text.Json.JsonValueKind.Number)
                            sold.Add(id.GetInt64());
                }
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
                    "{\"totalCredits\":" + balance + ",\"currencies\":" + CurrenciesJson(balance) +
                    ",\"items\":" + soldSb + "}");
        }

        if (path.EndsWith("/item") && req.HttpMethod == "GET")
        {
            var wanted = new List<long>();
            foreach (string part in (req.QueryString["idList"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (long.TryParse(part.Trim(), out long wid)) wanted.Add(wid);

            var itemInventory = ClubStore.Get().Inventory;
            var itemRnd = new Random();
            long itemNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var itemSb = new StringBuilder("[");
            int written = 0;
            foreach (long wid in wanted)
            {
                RealPlayer player;
                int pile;
                int at = itemInventory.FindIndex(c => c.ItemId == wid);
                if (at >= 0) { player = itemInventory[at].Player; pile = itemInventory[at].Pile; }
                else if (ItemIds.TryResolve(wid, out player)) { pile = 1; }
                else continue;

                if (written > 0) itemSb.Append(',');
                itemSb.Append(BuildRealPlayerItem(itemRnd, player, wid, itemNow, pile));
                written++;
            }
            itemSb.Append(']');
            return ("application/json; charset=utf-8", "{\"itemData\":" + itemSb + "}");
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

                var picks = PackEngine.Open(packId, rnd, out _);

                var drawn = new List<(long Id, string Json)>();
                var dupes = new List<(long NewId, long OwnedId)>();
                ClubStore.Mutate(data =>
                {
                    var ownedByCard = new Dictionary<int, long>();
                    foreach (var c in data.Inventory)
                        if (!ownedByCard.ContainsKey(c.Player.CardId))
                            ownedByCard[c.Player.CardId] = c.ItemId;

                    long nextPackItemId = data.Inventory.Where(c => ItemIds.IsPackItem(c.ItemId))
                                                        .Select(c => c.ItemId)
                                                        .DefaultIfEmpty(ItemIds.PackItemBase - 1)
                                                        .Max() + 1;

                    foreach (var pick in picks)
                    {
                        switch (pick.Kind)
                        {
                            case PackPick.ItemKind.Player:
                            {
                                long itemId = nextPackItemId++;
                                var player = pick.Player;
                                if (ownedByCard.TryGetValue(player.CardId, out long ownedId))
                                    dupes.Add((itemId, ownedId));
                                else
                                    ownedByCard[player.CardId] = itemId;
                                drawn.Add((itemId, BuildRealPlayerItem(rnd, player, itemId, nowUnix, 1)));
                                data.Inventory.Add(new ClubItem(itemId, player, 6));
                                break;
                            }
                            case PackPick.ItemKind.Consumable:
                            {
                                long id = Interlocked.Increment(ref _nextPackExtraId);
                                var inst = pick.Consumable with { ItemId = id };
                                drawn.Add((id, ConsumableItems.BuildJson(inst, nowUnix)));
                                data.Consumables.Add(inst);
                                break;
                            }
                            case PackPick.ItemKind.Cosmetic:
                            {
                                long id = Interlocked.Increment(ref _nextPackExtraId);
                                var inst = pick.Cosmetic with { ItemId = id };
                                drawn.Add((id, ClubItems.BuildJson(inst, nowUnix)));
                                data.Cosmetics.Add(inst);
                                break;
                            }
                            case PackPick.ItemKind.Manager:
                            {
                                long id = Interlocked.Increment(ref _nextPackExtraId);
                                drawn.Add((id, BuildManagerItem(pick.Manager, id, nowUnix, 6, pick.ManagerRareFlag)));
                                data.Managers.Add(pick.Manager);
                                break;
                            }
                            case PackPick.ItemKind.Staff:
                            {
                                long id = Interlocked.Increment(ref _nextPackExtraId);
                                drawn.Add((id, BuildStaffItem(pick.Staff, id, nowUnix, 6)));
                                data.Staff.Add(pick.Staff);
                                break;
                            }
                        }
                    }
                });

                var itemIds = new StringBuilder("[");
                var items = new StringBuilder("[");
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
                string name = null, formation = null;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(req.Body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.Number
                        && idEl.TryGetInt32(out int wantId) && wantId >= 0 && data.Squads.All(s => s.Id != wantId))
                        newId = wantId;
                    if (root.TryGetProperty("squadName", out var nEl) && nEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        name = nEl.GetString();
                    if (root.TryGetProperty("formation", out var fEl) && fEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        formation = fEl.GetString();
                }
                catch (Exception ex) { _log.LogWarning("Squad POST body parse failed: {0}", ex.Message); }

                created = new Squad { Id = newId };
                if (!string.IsNullOrWhiteSpace(name)) created.Name = name;
                if (!string.IsNullOrWhiteSpace(formation)) created.Formation = formation;
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
                if (data.Inventory.Count == 0) return;

                target = data.Squads.FirstOrDefault(s => s.Id == putId);
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

                    if (target == null)
                    {
                        target = new Squad { Id = putId };
                        data.Squads.Add(target);
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
                            int had = target.Slots.Count(s => s.Value != 0);
                            if (newSlots.Count < had)
                                _log.LogWarning("[FUT] squad PUT {0} shrinks the squad: {1} slot(s) in, {2} saved - the client dropped entries from our last squad body",
                                    putId, newSlots.Count, had);
                            else
                                _log.LogInformation("[FUT] squad PUT {0}: {1} slot(s) (was {2})", putId, newSlots.Count, had);

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
                if (target != null && target.Slots.Count > 0) data.ActiveSquadId = putId;
            });
            target ??= new Squad { Id = putId };   // nothing owned/persisted: respond with an empty squad
            return ("application/json; charset=utf-8", BuildFullSquadJson(target));
        }

        {
            var mTotw = System.Text.RegularExpressions.Regex.Match(path, @"squad/(\d+)/user/(\d+)");
            if (mTotw.Success && req.HttpMethod == "GET" && mTotw.Groups[2].Value == Totw.ClubPersona.ToString())
            {
                int week = int.Parse(mTotw.Groups[1].Value);
                _log.LogInformation("[TOTW] squad fetch: week {0}", week);
                return ("application/json; charset=utf-8", Totw.SquadForWeek(week));
            }
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"squad/(\d+)") && req.HttpMethod == "GET")
        {
            int getId = int.Parse(System.Text.RegularExpressions.Regex.Match(path, @"squad/(\d+)").Groups[1].Value);
            Squad target = null;
            ClubStore.Mutate(data =>
            {
                target = data.Squads.FirstOrDefault(s => s.Id == getId);
                if (target != null && data.ActiveSquadId != getId)
                {
                    data.ActiveSquadId = getId;
                    _log.LogInformation("[FUT] active squad -> {0} (loaded/equipped)", getId);
                }
            });
            target ??= new Squad { Id = getId };
            return ("application/json; charset=utf-8", BuildFullSquadJson(target));
        }

        if (path.EndsWith("/squad/active"))
        {
            var data = ClubStore.Get();
            var active = data.Squads.FirstOrDefault(s => s.Id == data.ActiveSquadId)
                ?? data.Squads.FirstOrDefault(s => s.Slots.Count > 0)
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
                string sqName = string.IsNullOrWhiteSpace(s.Name) ? "Squad 1" : s.Name;
                sb.Append("{\"id\":" + s.Id + ",\"squadId\":" + s.Id + ",\"squadName\":\"" + Esc(sqName) +
                    "\",\"formation\":\"" + s.Formation +
                    "\",\"chemistry\":" + s.Chemistry + ",\"rating\":" + s.StarRating +
                    ",\"starRating\":" + s.StarRating + ",\"squadType\":\"REGULAR_SQUAD\"}");
            }
            sb.Append(']');
            return ("application/json; charset=utf-8",
                    "{\"squadList\":" + sb + ",\"squad\":" + sb + "}");
        }

        // FUT user profile (/fut/rs4/ut/game/fifa14/user, .../userdata). Data-driven from the
        // profile: isReturningUser=false => NEW player (client state STATE_WELCOME, not
        // WELCOMEBACK — field name confirmed in fifa14.exe @ 0x1019992c). The parser hashes
        // field names and skips unknown ones, so extra fields are harmless.
        if (path.EndsWith("/user") || path.EndsWith("/userdata"))
        {
            if (req.HttpMethod == "POST" && req.Body.Contains("clubName"))
            {
                bool firstTime = !FutProfileStore.Get().Club.Established;
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
                if (firstTime) ClubStore.SeedStarterSquad();   // grant the bronze starter squad once
            }

            var prof = FutProfileStore.Get();
            return ("application/json; charset=utf-8",
                    "{\"isReturningUser\":" + (prof.IsReturningUser ? "true" : "false") +
                    ",\"established\":" + (prof.Club.Established ? "true" : "false") +
                    ",\"coins\":" + prof.Coins + ",\"credits\":" + prof.Coins +
                    ",\"currencies\":" + CurrenciesJson(prof.Coins) +
                    ",\"clubName\":\"" + Esc(prof.Club.Name) + "\",\"clubAbbr\":\"" + Esc(prof.Club.Abbr) + "\"" +
                    ",\"userAccountInfo\":" + UserAccountInfoJson(BlazePersonaId) + "}");
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
        {
            var pc = FutProfileStore.Get().Club;
            return "{\"clubId\":" + pc.TeamId + ",\"clubName\":\"" + Esc(pc.Name) + "\",\"leagueId\":0," +
                   "\"globalLeagueId\":0,\"division\":1,\"newDivision\":1,\"prevLeagueId\":0}";
        }
        if (path.Contains("/pfyc/schedule"))                      return "{\"schedule\":[]}";
        if (path.Contains("/pfyc/user/club"))
        {
            var pc = FutProfileStore.Get().Club;
            return "{\"clubId\":" + pc.TeamId + ",\"clubName\":\"" + Esc(pc.Name) + "\",\"leagueId\":0,\"globalLeagueId\":0,\"division\":1}";
        }
        if (path.Contains("/pfyc/user"))
        {
            var pc = FutProfileStore.Get().Club;
            long nuc = ParseLong(req.QueryString["friendtiertp"], BlazePersonaId);
            long pfycClubId = pc.TeamId;
            return "{\"users\":[{\"nucId\":" + nuc + ",\"clubId\":" + pfycClubId + ",\"pendingClubId\":0," +
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
        (100, "bronze",     400, false,   1,   0,   0,   4,   1,   0,   0,   0, ""),  // Bronze Pack
        (103, "bronze",     750, true,    1,   0,   0,   4,   1,   0,   0,   0, ""),  // Premium Bronze Pack
        (200, "silver",    2500, false,   2,   0,   4,   0,   1,   0,   0,   0, ""),  // Silver Pack
        (203, "silver",    3750, true,    2,   0,   4,   0,   1,   0,   0,   0, ""),  // Premium Silver Pack
        (300, "gold",      5000, false,   3,   4,   0,   0,   1,   0,   0,   0, ""),  // Gold Pack
        (304, "gold",      7500, true,    3,   4,   0,   0,   1,   3,   0,   0, ""),  // Premium Gold Pack
        (405, "special",  35000, true,    4,  12,   0,   0,   8,   6,   0,   0, ""),  // 30k Pack - 12 players, 8 rare minimum; 20% chance of one silver
        (406, "special",  50000, true,    5,  24,   0,   0,  24,   9,   0,  75, ""),  // Jumbo Rare Players - 24 rare gold
        (404, "special", 100000, true,    6,  30,   0,   0,  30,  12,   0,  76, ""),  // Mega Pack - the 50k, a bit better: 30 items, floor 76, slightly better special odds
    };

    private static readonly Dictionary<int,
        (int Contracts, int Fitness, int Training, int Healing, int Special, int RareExtras)> PackExtras =
        new()
        {
            //     contracts, fitness, training, healing, special, rareExtras
            [100] = (4, 1, 1, 1, 1, 0),   // Bronze Pack
            [103] = (4, 1, 1, 1, 1, 2),   // Premium Bronze Pack (3 rares)
            [200] = (4, 1, 1, 1, 1, 0),   // Silver Pack
            [203] = (4, 1, 1, 1, 1, 2),   // Premium Silver Pack (3 rares)
            [300] = (4, 1, 1, 1, 1, 0),   // Gold Pack
            [304] = (4, 1, 1, 1, 1, 2),   // Premium Gold Pack (3 rares)
        };

    private static int PackExtrasCount(int packId) =>
        PackExtras.TryGetValue(packId, out var e)
            ? e.Contracts + e.Fitness + e.Training + e.Healing + e.Special : 0;

    // Displayed rare count on the store tile: guaranteed rare players plus the rare consumables.
    private static int PackRareCount(int packId, int playerRare) =>
        playerRare + (PackExtras.TryGetValue(packId, out var e) ? e.RareExtras : 0);

    private static long _nextPackExtraId = 970_000_000L;


    private static string DuplicateListJson(IEnumerable<(long NewId, long OwnedId)> dupes)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var (newId, ownedId) in dupes)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"duplicateItemId\":" + ownedId + ",\"itemId\":" + newId + "}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string BodyRx(string s, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", pattern);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string MatchEndBody(long balance, int matchCoins, int tournamentCoins = 0) =>
        "{\"allCoins\":" + balance + ",\"matchCoins\":" + matchCoins + ",\"seasonCoins\":" + balance +
        ",\"tournamentCoins\":" + tournamentCoins + ",\"boostConis\":0,\"boostCountLeft\":0,\"participationAward\":" + matchCoins +
        ",\"matchCoinPartials\":[],\"matchCoinMultipliers\":[],\"matchParamsKeyValues\":{}," +
        "\"endReason\":\"FT\",\"credits\":" + balance + ",\"coins\":" + balance +
        ",\"currencies\":" + CurrenciesJson(balance) +
        ",\"userData\":{\"credits\":" + balance + ",\"coins\":" + balance + "}}";

    private static string NoTransactionBody() =>
        "{\"transactionId\":0,\"state\":\"NOTRANSACTION\",\"packId\":0,\"purchasePackType\":\"\"," +
        "\"firstPartyStoreId\":0,\"useAuth\":0,\"useCount\":0,\"useTime\":0}";

    private static string UserAccountInfoJson(long nucleusId)
    {
        var prof = FutProfileStore.Get();
        string ret = prof.IsReturningUser ? "true" : "false";
        int est = prof.Club.Established ? 1 : 0;
        const string Sku = "FFA14PCC";
        string clubList =
            "{\"year\":2014,\"teamId\":" + prof.Club.TeamId +
            ",\"teamName\":\"" + Esc(prof.Club.Name) + "\",\"clubName\":\"" + Esc(prof.Club.Name) + "\"," +
            "\"clubAbbr\":\"" + Esc(prof.Club.Abbr) + "\",\"clubId\":" + prof.Club.TeamId +
            ",\"platform\":\"pc\",\"assetId\":" + prof.Club.BadgeId + ",\"badgeId\":" + prof.Club.BadgeId +
            ",\"seasonId\":1,\"status\":" + est + ",\"established\":" + est + ",\"divisionOnline\":1" +
            ",\"divisionOffline\":" + prof.OfflineDivision + ",\"lastAccessTime\":1400000000," +
            "\"skuAccessList\":{\"" + Sku + "\":1,\"FFA14PS3\":1,\"FFA14XBX\":1}}";
        string clubListEntries = prof.Club.Established ? clubList : "";
        string persona =
            "{\"personaId\":" + nucleusId + ",\"personaName\":\"" + BlazePersonaName + "\"," +
            "\"returningUser\":" + ret + ",\"isReturningUser\":" + ret + ",\"trial\":false,\"userState\":\"\"," +
            "\"userClubList\":[" + clubListEntries + "]}";
        return "{\"personas\":[" + persona + "],\"userPersonaInfos\":[]}";
    }

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
            int rare   = PackRareCount(p.Id, p.Rare);
            int items  = gold + silver + bronze + PackExtrasCount(p.Id);
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

    internal static string BuildRealPlayerItem(Random rnd, RealPlayer player, long id, long timestamp, int pile)
    {
        int rating = player.Rating;
        int assetId = player.Id;
        int resourceId = player.CardId;
        int rareflag = player.Rare;
        int[] attrs = { player.Pace, player.Shooting, player.Passing, player.Dribbling, player.Defending, player.Physical };

        int contract = 7, fitness = 99, playStyle = 250, injuryGames = 0, training = 0;
        string position = player.Position, injuryType = "none";
        if (ClubStore.Get().PlayerMods.TryGetValue(id, out var mod) && mod != null)
        {
            if (mod.PlayStyle >= 0) playStyle = mod.PlayStyle;
            if (!string.IsNullOrEmpty(mod.Position)) position = mod.Position;
            if (mod.Contract >= 0) contract = mod.Contract;
            if (mod.Fitness >= 0) fitness = mod.Fitness;
            if (mod.AttrBoost != null)
                for (int a = 0; a < 6 && a < mod.AttrBoost.Length; a++)
                    attrs[a] = Math.Clamp(attrs[a] + mod.AttrBoost[a], 1, 99);
            if (mod.TrainingFlag > 0) training = mod.TrainingFlag;   // flags an active "next match" boost
            if (!string.IsNullOrEmpty(mod.Injury) && mod.InjuryGames > 0)
            {
                injuryType = mod.Injury;
                injuryGames = mod.InjuryGames;
            }
        }

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
            "\"lastSalePrice\":0,\"morale\":50,\"fitness\":" + fitness + ",\"injuryType\":\"" + injuryType + "\",\"injuryGames\":" + injuryGames + "," +
            "\"preferredPosition\":\"" + position + "\",\"statsList\":" + zeroStats +
            ",\"lifetimeStats\":" + zeroStats + ",\"training\":" + training + ",\"contract\":" + contract + ",\"suspension\":0," +
            "\"marketDataMinPrice\":150,\"marketDataMaxPrice\":15000000,\"attributeList\":" + attrList +
            ",\"teamid\":" + player.TeamId + ",\"rareflag\":" + rareflag + ",\"playStyle\":" + playStyle + "," +
            "\"leagueId\":1,\"assists\":0,\"lifetimeAssists\":0," +
            "\"loyaltyBonus\":1,\"pile\":" + pile + ",\"loans\":0,\"nation\":" + player.NationId +
            ",\"resourceGameYear\":2014,\"amount\":0}";
    }

    private static List<ConsumableItem> AvailableConsumables()
    {
        var owned = ClubStore.Get().Consumables;
        var ownedRes = new HashSet<long>(owned.Select(c => c.ResourceId));
        var list = new List<ConsumableItem>(owned);
        foreach (var c in ConsumableItems.Catalog)
            if (!ownedRes.Contains(c.ResourceId)) list.Add(c);
        return list;
    }

    private static Func<ConsumableItem, bool> ConsumableTabFilter(string tab)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        static bool Is(string it, string p) => (it ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase);
        if (tab.Contains("contract")) return c => Is(c.ItemType, "Contract");
        if (tab.Contains("fitness")) return c => Is(c.ItemType, "Fitness");
        if (tab.Contains("heal") || tab.Contains("health")) return c => Is(c.ItemType, "Health");
        if (tab.Contains("position")) return c => Is(c.ItemType, "TrainingPlayerPos");
        if (tab.Contains("chem") || tab.Contains("style") || tab.Contains("playstyle"))
            return c => string.Equals(c.ItemType, "playStyle", OIC);
        if (tab.Contains("manager") || tab.Contains("league") || tab.Contains("staff"))
            return c => string.Equals(c.ItemType, "managerLeagueModifier", OIC);
        if (tab.Contains("training"))
            return c => (Is(c.ItemType, "TrainingPlayer") || Is(c.ItemType, "TrainingGk"))
                        && !Is(c.ItemType, "TrainingPlayerPos");
        return null;   // bare /consumables or an unrecognised tab -> the whole catalog
    }

    private static List<long> ApplyConsumable(long resourceId, List<long> targets)
    {
        var changed = new List<long>();
        if (resourceId <= 0) return changed;
        var c = ConsumableItems.Catalog.FirstOrDefault(x => x.ResourceId == resourceId);
        if (c.ResourceId != resourceId)
        {
            Console.WriteLine($"[FUT] apply consumable: unknown resourceId {resourceId}");
            return changed;
        }
        ConsumableItems.Effects.TryGetValue(resourceId, out var def);
        bool teamFitness = def.Category == "fitness"
                           && string.Equals(def.Kind, "Squad", StringComparison.OrdinalIgnoreCase);
        var applyTo = teamFitness ? ActiveSquadItemIds() : targets;
        if (applyTo == null || applyTo.Count == 0) return changed;

        ClubStore.Mutate(data =>
        {
            foreach (long tid in applyTo)
            {
                int pi = data.Inventory.FindIndex(x => x.ItemId == tid);
                if (pi < 0) continue;   // not an owned player (e.g. a manager) -> skip
                int rating = data.Inventory[pi].Player.Rating;
                if (!data.PlayerMods.TryGetValue(tid, out var mod) || mod == null)
                {
                    mod = new PlayerMod();
                    data.PlayerMods[tid] = mod;
                }
                ApplyEffect(mod, rating, c);
                changed.Add(tid);
            }
        });
        Console.WriteLine($"[FUT] applied consumable {resourceId} ({def.Category}/{def.Kind}) to {changed.Count} player(s)");
        return changed;
    }

    private static string AppliedItemsJson(long resourceId, List<long> changedIds)
    {
        var data = ClubStore.Get();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rnd = new Random();
        var sb = new StringBuilder("[");
        int n = 0;
        foreach (long tid in changedIds)
        {
            int pi = data.Inventory.FindIndex(x => x.ItemId == tid);
            if (pi < 0) continue;
            if (n++ > 0) sb.Append(',');
            sb.Append(BuildRealPlayerItem(rnd, data.Inventory[pi].Player, tid, now, data.Inventory[pi].Pile));
        }
        sb.Append(']');
        return "{\"success\":true,\"resourceId\":" + resourceId + ",\"itemData\":" + sb + "}";
    }

    private static void ApplyEffect(PlayerMod mod, int targetRating, ConsumableItem c)
    {
        if (string.Equals(c.ItemType, "playStyle", StringComparison.OrdinalIgnoreCase))
        {
            mod.PlayStyle = c.SubType;
            return;
        }
        if (!ConsumableItems.Effects.TryGetValue(c.ResourceId, out var def))
            return;   // no modifier def and not a chem style -> nothing to apply

        switch (def.Category)
        {
            case "contract":   // gain depends on the TARGET player's quality tier
            {
                int gain = targetRating <= 64 ? def.Bronze : targetRating <= 74 ? def.Silver : def.Gold;
                int cur = mod.Contract >= 0 ? mod.Contract : 7;
                mod.Contract = Math.Min(99, cur + Math.Max(0, gain));
                break;
            }
            case "fitness":
            {
                int cur = mod.Fitness >= 0 ? mod.Fitness : 99;
                mod.Fitness = Math.Min(99, cur + Math.Max(0, def.Amount));
                break;
            }
            case "healing":            // reduce the injury only if the card's body part matches
            {
                if (mod.InjuryGames > 0
                    && (string.Equals(def.Kind, "All", StringComparison.OrdinalIgnoreCase)
                        || InjuryMatches(mod.Injury, def.Kind)))
                {
                    mod.InjuryGames = Math.Max(0, mod.InjuryGames - Math.Max(1, def.Amount));
                    if (mod.InjuryGames == 0) mod.Injury = "";
                }
                break;
            }
            case "position":
            {
                string pos = NewPositionFromKind(def.Kind);
                if (pos.Length > 0) mod.Position = pos;
                break;
            }
            case "chemstyle":
                mod.PlayStyle = def.CardSubtypeId != 0 ? def.CardSubtypeId : c.SubType;
                break;
            case "training":
            case "gktraining":
            {
                int amount = Math.Max(0, def.Amount);
                int idx = AttrIndexForKind(def.Kind);        // -1 = ALL, -2 = unmapped
                if (idx == -1) for (int a = 0; a < 6; a++) mod.AttrBoost[a] = amount;
                else if (idx >= 0) mod.AttrBoost[idx] = amount;   // one active boost per attribute (replace)
                if (idx >= -1) mod.TrainingFlag = c.SubType;  // flag the active "next match" boost
                break;
            }
        }
    }

    private static string NewPositionFromKind(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return "";
        var parts = kind.Split(new[] { '→', '>', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1].Trim().ToUpperInvariant() : "";
    }

    private static int AttrIndexForKind(string kind) => (kind ?? "").ToUpperInvariant() switch
    {
        "ALL" => -1,
        "PAC" or "DIV" => 0,
        "SHO" or "HAN" => 1,
        "PAS" or "KIC" => 2,
        "DRI" or "REF" => 3,
        "DEF" or "SPD" => 4,
        "PHY" or "POS" => 5,
        _ => -2,
    };

    private static List<long> ActiveSquadItemIds()
    {
        var data = ClubStore.Get();
        var sq = data.Squads.FirstOrDefault(s => s.Id == data.ActiveSquadId)
                 ?? data.Squads.FirstOrDefault();
        if (sq == null) return new List<long>();
        return sq.Slots.Values.Where(v => v > 0).Distinct().ToList();
    }

    private static List<long> ActiveSquadStarterIds()
    {
        var data = ClubStore.Get();
        var sq = data.Squads.FirstOrDefault(s => s.Id == data.ActiveSquadId)
                 ?? data.Squads.FirstOrDefault();
        if (sq == null) return new List<long>();
        return sq.Slots.Where(kv => kv.Key < 11 && kv.Value > 0).Select(kv => kv.Value).Distinct().ToList();
    }

    private static List<long> ActiveSquadBenchIds()
    {
        var data = ClubStore.Get();
        var sq = data.Squads.FirstOrDefault(s => s.Id == data.ActiveSquadId)
                 ?? data.Squads.FirstOrDefault();
        if (sq == null) return new List<long>();
        return sq.Slots.Where(kv => kv.Key >= 11 && kv.Key <= 17 && kv.Value > 0)
                       .Select(kv => kv.Value).Distinct().ToList();
    }

    private static readonly (string Kind, string[] Tokens)[] InjuryGroups =
    {
        ("Head",      new[] { "head", "concussion" }),
        ("Arm",       new[] { "wrist", "hand", "elbow" }),
        ("UpperBody", new[] { "shoulder", "back", "rib" }),
        ("Knee",      new[] { "knee" }),
        ("Leg",       new[] { "hamstring", "thigh", "calf", "groin" }),
        ("Foot",      new[] { "ankle", "toe", "foot" }),
    };

    private static void ApplyMatchConsequences()
    {
        var xi = ActiveSquadStarterIds();
        var bench = ActiveSquadBenchIds();
        if (xi.Count == 0 && bench.Count == 0) return;
        var rnd = new Random();
        int played = 0, benched = 0;
        ClubStore.Mutate(data =>
        {
            PlayerMod ModFor(long tid)
            {
                if (data.Inventory.FindIndex(x => x.ItemId == tid) < 0) return null;   // owned players only
                if (!data.PlayerMods.TryGetValue(tid, out var m) || m == null)
                {
                    m = new PlayerMod();
                    data.PlayerMods[tid] = m;
                }
                return m;
            }
            foreach (long tid in xi)
            {
                var mod = ModFor(tid);
                if (mod == null) continue;
                mod.Contract = Math.Max(0, (mod.Contract >= 0 ? mod.Contract : 7) - 1);
                mod.Fitness = Math.Max(0, (mod.Fitness >= 0 ? mod.Fitness : 99) - rnd.Next(8, 13));
                if (mod.TrainingFlag > 0) { System.Array.Clear(mod.AttrBoost, 0, mod.AttrBoost.Length); mod.TrainingFlag = 0; }
                played++;
            }
            foreach (long tid in bench)
            {
                var mod = ModFor(tid);
                if (mod == null) continue;
                mod.Contract = Math.Max(0, (mod.Contract >= 0 ? mod.Contract : 7) - 1);   // rostered -> contract only
                benched++;
            }
        });
        Console.WriteLine($"[FUT] match consequences: {played} played (fitness+contract), {benched} subs (contract)");
    }

    private static bool InjuryMatches(string injury, string cardKind)
    {
        if (string.IsNullOrEmpty(injury)) return false;
        foreach (var (kind, tokens) in InjuryGroups)
            if (string.Equals(kind, cardKind, StringComparison.OrdinalIgnoreCase))
                return System.Array.Exists(tokens, t => string.Equals(t, injury, StringComparison.OrdinalIgnoreCase));
        return false;
    }

    private static string ConsumableStatsJson()
    {
        int contractPlayer = 0, contractManager = 0, fitnessPlayer = 0, fitnessTeam = 0, healing = 0;
        int trainingPlayer = 0, trainingGk = 0, position = 0, playStyle = 0, managerLeague = 0, formation = 0;
        foreach (var c in AvailableConsumables())
        {
            string t = c.ItemType ?? "";
            if (t.StartsWith("ContractStaff", StringComparison.OrdinalIgnoreCase)) contractManager++;
            else if (t.StartsWith("Contract", StringComparison.OrdinalIgnoreCase)) contractPlayer++;
            else if (t.StartsWith("FitnessTeam", StringComparison.OrdinalIgnoreCase)) fitnessTeam++;
            else if (t.StartsWith("Fitness", StringComparison.OrdinalIgnoreCase)) fitnessPlayer++;
            else if (t.StartsWith("Health", StringComparison.OrdinalIgnoreCase)) healing++;
            else if (t.StartsWith("TrainingPlayerPos", StringComparison.OrdinalIgnoreCase)) position++;
            else if (t.StartsWith("TrainingGk", StringComparison.OrdinalIgnoreCase)) trainingGk++;
            else if (t.StartsWith("TrainingPlayer", StringComparison.OrdinalIgnoreCase)) trainingPlayer++;
            else if (t.Equals("playStyle", StringComparison.OrdinalIgnoreCase)) playStyle++;
            else if (t.Equals("managerLeagueModifier", StringComparison.OrdinalIgnoreCase)) managerLeague++;
            else if (t.Equals("formation", StringComparison.OrdinalIgnoreCase)) formation++;
        }
        int total = AvailableConsumables().Count;
        var members = new (string Key, int Val)[]
        {
            ("consumablesContractPlayer", contractPlayer),
            ("consumablesContractManager", contractManager),
            ("consumablesFitnessPlayer", fitnessPlayer),
            ("consumablesFitnessTeam", fitnessTeam),
            ("consumablesHealing", healing),
            ("consumablesTrainingPlayer", trainingPlayer),
            ("consumablesTrainingGk", trainingGk),
            ("consumablesTrainingPlayerPlayStyle", playStyle),
            ("consumablesTrainingGkPlayStyle", playStyle),
            ("consumablesPosition", position),
            ("consumablesTrainingManager", managerLeague),
            ("consumablesTrainingManagerLeagueModifier", managerLeague),
            ("consumablesFormationManager", formation),
            ("consumablesContract", contractPlayer + contractManager),
            ("consumablesFitness", fitnessPlayer + fitnessTeam),
            ("consumablesTraining", trainingPlayer + trainingGk),
            ("consumables", total),
        };
        var scalars = string.Join(",", members.Select(x => "\"" + x.Key + "\":" + x.Val));
        var entries = "[" + string.Join(",", members.Select(x =>
            "{\"contextId\":6,\"contextValue\":0,\"type\":\"" + x.Key + "\",\"typeValue\":" + x.Val + "}")) + "]";
        return "{" + scalars + ",\"stat\":" + entries + ",\"entries\":" + entries + "}";
    }

    internal static string BuildManagerItem(Manager m, long id, long timestamp, int pile, int rareflag = 1)
    {
        return "{\"id\":" + id + ",\"timestamp\":" + timestamp + ",\"formation\":\"f442\"," +
            "\"untradeable\":false,\"assetId\":" + m.ResourceId + ",\"rating\":" + m.Rating + "," +
            "\"itemType\":\"manager\",\"dream\":false,\"resourceId\":" + m.ResourceId + ",\"owners\":1," +
            "\"discardValue\":" + (m.Rating * 4) + ",\"itemState\":\"free\",\"cardsubtypeid\":4," +
            "\"lastSalePrice\":0,\"morale\":0,\"fitness\":0,\"injuryType\":\"none\",\"injuryGames\":0," +
            "\"preferredPosition\":\"\",\"statsList\":[],\"lifetimeStats\":[],\"training\":0," +
            "\"contract\":7,\"suspension\":0,\"marketDataMinPrice\":150,\"marketDataMaxPrice\":15000000," +
            "\"attributeList\":[],\"teamid\":0,\"rareflag\":" + rareflag + ",\"playStyle\":0," +
            "\"leagueId\":" + m.LeagueId + ",\"leagueid\":" + m.LeagueId + "," +
            "\"assists\":0,\"lifetimeAssists\":0,\"loyaltyBonus\":1,\"pile\":" + pile + ",\"loans\":0," +
            // nationid = manager card's flag slot (lowercase). Send nation too for other consumers.
            "\"nation\":" + m.NationId + ",\"nationid\":" + m.NationId +
            ",\"resourceGameYear\":2014,\"amount\":0}";
    }

    internal const long ManagerItemIdBase = 640_000L;

    private static string ManagerItemsJson(int offset, int countLimit, long now, int pile, int nationFilter = -1, int leagueFilter = -1, string levelFilter = "")
    {
        var page = ClubStore.Get().Managers
            .Where(m => (nationFilter == -1 || m.NationId == nationFilter)
                && (leagueFilter == -1 || m.LeagueId == leagueFilter)
                && levelFilter switch
                {
                    "bronze" => m.Rating < 65,
                    "silver" => m.Rating is >= 65 and < 75,
                    "gold" => m.Rating >= 75,
                    _ => true,
                })
            .Skip(offset).Take(countLimit).ToArray();
        var sb = new StringBuilder("[");
        for (int i = 0; i < page.Length; i++)
        {
            if (i > 0) sb.Append(',');
            int idx = offset + i;
            sb.Append(BuildManagerItem(page[i], ManagerItemIdBase + idx, now, pile));
        }
        sb.Append(']');
        return sb.ToString();
    }

    internal static string BuildStaffItem(StaffCard s, long id, long timestamp, int pile)
    {
        bool boost = s.ItemType == "physio" || s.ItemType == "fitnessCoach";
        string attrList = "[]";
        if (boost)
        {
            var sb = new StringBuilder("[");
            for (int a = 0; a <= 6; a++)
            {
                if (a > 0) sb.Append(',');
                sb.Append("{\"value\":" + (a == s.Attr ? s.Amount : 0) + ",\"index\":" + a + "}");
            }
            sb.Append(']');
            attrList = sb.ToString();
        }
        int amount = boost ? s.Amount : 0;

        string extra = "";
        if (s.ItemType == "physio")
        {
            // physio DB: attribute (body part 0-6) + amount (heal). Put amount on the matching Attribute slot.
            var a = new int[6];
            if (s.Attr >= 0 && s.Attr < 6) a[s.Attr] = s.Amount;
            extra = ",\"Attribute1\":" + a[0] + ",\"Attribute2\":" + a[1] + ",\"Attribute3\":" + a[2] +
                    ",\"Attribute4\":" + a[3] + ",\"Attribute5\":" + a[4] + ",\"Attribute6\":" + a[5] +
                    ",\"statBonus\":" + s.Amount + ",\"bonus\":" + s.Amount;
        }
        else if (s.ItemType == "fitnessCoach")
        {
            // fitnessCoach DB: amount + posbonus + fieldpos (no attribute).
            extra = ",\"statBonus\":" + s.Amount + ",\"bonus\":" + s.PosBonus + ",\"posMods\":" + s.PosBonus +
                    ",\"position\":" + s.FieldPos + ",\"gkPositioning\":" + s.FieldPos;
        }
        return "{\"id\":" + id + ",\"timestamp\":" + timestamp + ",\"formation\":\"f442\"," +
            "\"untradeable\":false,\"assetId\":" + s.ResourceId + ",\"rating\":" + s.Rating + "," +
            "\"itemType\":\"" + Esc(s.ItemType) + "\",\"dream\":false,\"resourceId\":" + s.ResourceId + ",\"owners\":1," +
            "\"discardValue\":" + (s.Rating * 4) + ",\"itemState\":\"free\",\"cardsubtypeid\":" + s.CardSubType + "," +
            "\"lastSalePrice\":0,\"morale\":0,\"fitness\":0,\"injuryType\":\"none\",\"injuryGames\":0," +
            "\"preferredPosition\":\"\",\"statsList\":[],\"lifetimeStats\":[],\"training\":0," +
            "\"contract\":7,\"suspension\":0,\"marketDataMinPrice\":150,\"marketDataMaxPrice\":15000000," +
            "\"attributeList\":" + attrList + ",\"teamid\":0,\"rareflag\":" + s.Rare + ",\"playStyle\":0," +
            "\"leagueId\":0,\"leagueid\":0,\"assists\":0,\"lifetimeAssists\":0,\"loyaltyBonus\":1," +
            "\"pile\":" + pile + ",\"loans\":0,\"nation\":0,\"nationid\":0," +
            "\"resourceGameYear\":2014,\"amount\":" + amount + extra + "}";
    }

    internal const long StaffItemIdBase = 650_000L;

    private static string StaffItemsJson(int offset, int countLimit, long now, int pile, string typeFilter = null, string levelFilter = "")
    {
        var data = ClubStore.Get();
        var all = new List<string>(data.Managers.Count + data.Staff.Count);
        if (typeFilter == null)
        {
            for (int i = 0; i < data.Managers.Count; i++)
                all.Add(BuildManagerItem(data.Managers[i], ManagerItemIdBase + i, now, pile));
            for (int i = 0; i < data.Staff.Count; i++)
                all.Add(BuildStaffItem(data.Staff[i], StaffItemIdBase + i, now, pile));
        }
        else
        {
            for (int i = 0; i < data.Staff.Count; i++)
            {
                var s = data.Staff[i];
                if (!string.Equals(s.ItemType, typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
                bool levelOk = levelFilter switch
                {
                    "bronze" => s.Rating < 65,
                    "silver" => s.Rating is >= 65 and < 75,
                    "gold" => s.Rating >= 75,
                    _ => true,
                };
                if (!levelOk) continue;
                all.Add(BuildStaffItem(s, StaffItemIdBase + i, now, pile));
            }
        }
        var page = all.Skip(offset).Take(countLimit);
        return "[" + string.Join(",", page) + "]";
    }

    private static string BuildFullSquadJson(Squad squad)
    {
        var inventory = ClubStore.Get().Inventory;
        var rnd = new Random();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int slotCount = SquadSlots;
        if (squad.Slots.Count > 0) slotCount = Math.Max(slotCount, squad.Slots.Keys.Max() + 1);

        var playersSb = new StringBuilder("[");
        long captainId = 0;
        int filled = 0;
        for (int idx = 0; idx < slotCount; idx++)
        {
            if (idx > 0) playersSb.Append(',');
            squad.Slots.TryGetValue(idx, out long itemId);

            RealPlayer player = default;
            bool has = false;
            if (itemId != 0)
            {
                var member = inventory.FirstOrDefault(c => c.ItemId == itemId);
                if (member.ItemId != 0) { player = member.Player; has = true; }
                else has = ItemIds.TryResolve(itemId, out player);
            }

            if (!has)
            {
                playersSb.Append("{\"index\":" + idx + ",\"loyaltyBonus\":0,\"kitNumber\":0,\"chemistry\":0," +
                                 "\"itemData\":{\"id\":0}}");
                continue;
            }

            filled++;
            string item = BuildRealPlayerItem(rnd, player, itemId, now, 7);
            playersSb.Append("{\"index\":" + idx + ",\"loyaltyBonus\":1,\"kitNumber\":0,\"chemistry\":10,\"itemData\":" + item + "}");
            if (captainId == 0 || player.Position == "ST") captainId = itemId;
        }
        playersSb.Append(']');
        Console.WriteLine($"[FUT] squad {squad.Id}: {filled} of {slotCount} slots filled");
        // The client computes and PUTs its own chemistry/rating/starRating, so we just
        // persist and echo those back rather than recomputing server-side.
        int rating = squad.StarRating;

        string actives = ActivesJson(now);

        string kicktakers = "[{\"id\":" + captainId + ",\"index\":0},{\"id\":" + captainId + ",\"index\":1}," +
            "{\"id\":" + captainId + ",\"index\":2},{\"id\":" + captainId + ",\"index\":3}," +
            "{\"id\":" + captainId + ",\"index\":4}]";

        string squadManager = "{\"id\":0,\"itemType\":\"manager\"}";
        if (squad.ManagerId != 0)
        {
            int mIdx = (int)(squad.ManagerId - ManagerItemIdBase);
            if (mIdx >= 0 && mIdx < Managers.All.Length)
                squadManager = BuildManagerItem(Managers.All[mIdx], squad.ManagerId, now, 7);
        }

        return "{\"id\":" + squad.Id + ",\"valid\":true,\"personaId\":" + FutSquadPersonaId + ",\"formation\":\"" + squad.Formation +
            "\",\"rating\":" + rating + ",\"chemistry\":" + squad.Chemistry +
            ",\"manager\":[" + squadManager + "],\"players\":" + playersSb +
            ",\"actives\":" + actives + ",\"dreamSquad\":false,\"changed\":0,\"squadName\":\"" + Esc(squad.Name) + "\"," +
            "\"starRating\":" + rating + ",\"captain\":" + captainId + ",\"kicktakers\":" + kicktakers +
            ",\"squadType\":\"REGULAR_SQUAD\",\"newSquad\":null,\"custom\":null}";
    }


    private static string ActivesJson(long now)
    {
        var prof = FutProfileStore.Get();
        var sb = new StringBuilder("[");
        sb.Append(ActiveJson(800001, prof.Club.ActiveStadiumId, "stadium", "activeStadium", 10, now));
        sb.Append(',').Append(ActiveJson(800002, prof.Club.ActiveBallId, "ball", "activeBall", 30, now));
        sb.Append(',').Append(ActiveJson(800003, prof.Club.ActiveHomeKitId, "kit", "activeHomeKit", 9, now));
        sb.Append(',').Append(ActiveJson(800004, prof.Club.ActiveAwayKitId, "kit", "activeAwayKit", 9, now));
        sb.Append(',').Append(ActiveJson(800005, prof.Club.ActiveBadgeId, "badge", "activeBadge", 11, now));
        return sb.Append(']').ToString();
    }

    private static string ActiveJson(long itemId, long resourceId, string type, string state, int subType, long now)
    {
        var item = ClubItems.Catalog.FirstOrDefault(c => c.ResourceId == resourceId);
        if (item.ResourceId != resourceId)
        {
            Console.WriteLine($"[FUT] active {type} {resourceId} is not in the club item catalog");
            return "{}";
        }

        string head =
            "{\"id\":" + itemId + ",\"timestamp\":" + now + ",\"formation\":\"f442\",\"untradeable\":false," +
            "\"assetId\":" + item.AssetId + ",\"rating\":" + item.Rating + "," +
            "\"itemType\":\"" + (type == "badge" ? "custom" : type) + "\"," +
            "\"resourceId\":" + resourceId + ",\"owners\":1,\"discardValue\":110,\"itemState\":\"" + state + "\"," +
            "\"cardsubtypeid\":" + subType + ",\"lastSalePrice\":0,\"statsList\":[],\"lifetimeStats\":[]," +
            "\"attributeList\":[],\"teamid\":" + item.TeamId + ",\"rareflag\":" + item.Rare + ",\"leagueId\":0," +
            "\"pile\":7,\"resourceGameYear\":2014";

        return head + type switch
        {
            "stadium" => ",\"cardassetid\":36,\"category\":" + item.Category + ",\"name\":\"" + Esc(item.Name) +
                         "\",\"description\":\"StadiumDesc_" + item.AssetId + "\"," +
                         "\"biodescription\":\"StadiumDetailDesc\"," +
                         "\"stadiumid\":" + item.AssetId + ",\"capacity\":30000}",
            "ball"    => ",\"cardassetid\":37,\"category\":" + item.Category + ",\"name\":\"" + Esc(item.Name) +
                         "\",\"value\":" + item.Rating + ",\"manufacturer\":\"ManufacturerGeneric\"}",
            "kit"     => ",\"category\":" + item.Category + ",\"year\":0}",
            _         => ",\"category\":" + item.Category + ",\"value\":" + item.Rating +
                         ",\"weightrare\":" + (item.Rare * 10) + ",\"header\":\"Badge\"}",
        };
    }

    private static string CurrenciesJson(long coins) =>
        "[{\"name\":\"COINS\",\"funds\":" + coins + ",\"finalFunds\":" + coins + ",\"originalPrice\":" + coins + "}," +
        "{\"name\":\"POINTS\",\"funds\":0,\"finalFunds\":0,\"originalPrice\":0}," +
        "{\"name\":\"DRAFT_TOKEN\",\"funds\":0,\"finalFunds\":0,\"originalPrice\":0}]";

    // Keep these in sync with AuthenticationComponent (UserId / PersonaName) so the EASFC
    // web identity matches the Blaze-authenticated persona.
    private const int SquadSlots = 23;

    private const long FutSquadPersonaId = 0;

    private const long BlazePersonaId = 1000;
    private static readonly string BlazePersonaName = UserConfig.Username;

    private const string SessionId = "FIFA14SERVERSESSION0000000000000";

    private const string PowSid = "f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14f1fa14beef";

    private static long ParseLong(string s, long dflt) => long.TryParse(s, out var v) ? v : dflt;

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "...<truncated>";

    private static string Esc(string s) => (s ?? "")
        .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
