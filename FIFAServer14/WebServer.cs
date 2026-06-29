using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FIFAServer14;

internal sealed class WebServer
{
    private readonly ILogger _log;
    private readonly HttpListener _listener = new();
    private readonly int _port;

    public WebServer(int port, ILogger log)
    {
        _port = port;
        _log = log;
        // Loopback + a literal IP prefix binds without an admin urlacl reservation.
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
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

            var payload = Encoding.UTF8.GetBytes(payloadStr);
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

    private static (string, string) Route(HttpListenerRequest req)
    {
        string path = (req.Url?.AbsolutePath ?? "").ToLowerInvariant();
        bool wantsJson = (req.Headers["Accept"] ?? "").Contains("json", StringComparison.OrdinalIgnoreCase);

        // FUT/EASFC accountinfo. This is the EASFC backend ("rs4") handshake — the persona
        // here MUST match the one we authenticated over Blaze (AuthenticationComponent:
        // personaId=1000, name="FUT14"), or EASFC can't associate the session and shows
        // "unable to connect". The client sends its id in Easw-Session-Data-Nucleus-Id.
        // Field names confirmed in fifa14.exe: userAccountInfo/personas/personaId/
        // personaName/userClubList. Empty userClubList = new FUT user (no club yet).
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

        // DIME config-routing file. This is the game's own bundled cfgrouting.xml served
        // back over the network (the game fetches it from ROUTINGCFGFILE_URL). The *local
        // entries make DIME load the bundled dimecfg/storecfg from the game's data BIGs;
        // the network entries are skipped because we don't set their osdkVars
        // (DIME_FILES_PATH / FUTBOOTCFGFILE_URL) yet. Flip those later to serve our own.
        if (path.EndsWith("dimerouting.xml") || path.EndsWith("cfgrouting.xml"))
            return ("text/xml; charset=utf-8", DimeRoutingXml);

        if (path.EndsWith("futboot.xml"))
            return ("text/xml; charset=utf-8", FutBootXml);

        // Default JSON endpoints
        if (wantsJson || path.StartsWith("/fut"))
            return ("application/json; charset=utf-8", "{}");

        return ("text/xml; charset=utf-8", "");
    }

    // Verbatim from the game's bundled data7.big
    private const string DimeRoutingXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <routing fileVersion="1" refresh="1800">
        	<files>
        		<file service="fut" type="network" osdkVar="FUTBOOTCFGFILE_URL" defaultFile="futBoot.xml"/>
        		<file service="dimecfglocal" type="local" base="data/store/" defaultFile="dimecfg.xml"/>
        		<file service="storedesclocal" type="local" base="data/store/" defaultFile="storedesc-%s.xml"  modifier="locale"/>
        		<file service="storecfglocal" type="local" base="data/store/" defaultFile="storecfg.xml"/>
        		<file service="dimecfg" type="network" osdkVar="DIME_FILES_PATH" defaultFile="dimecfg.xml"/>
        		<file service="dimecfgbin" type="network" osdkVar="DIME_FILES_PATH" defaultFile="dimecfg.xml.bin"/>
        		<file service="storecfg" type="network" osdkVar="DIME_FILES_PATH" defaultFile="storecfg.xml"/>
        		<file service="storedesc" type="network" osdkVar="DIME_FILES_PATH" defaultFile="storedesc-%s.xml" modifier="locale"/>
        		<file service="storeimage" type="network" osdkVar="DIME_IMG_FILES_PATH" defaultFile="item_%s.big" modifier="custom"/>
        		<file service="audiodnp" type="network" osdkVar="DOWNLOADER_PATH" defaultFile="audioDNPList.csv"/>
        	</files>
        </routing>
        """;

    // Keep these in sync with AuthenticationComponent (UserId / PersonaName) so the EASFC
    // web identity matches the Blaze-authenticated persona.
    private const long BlazePersonaId = 1000;
    private const string BlazePersonaName = "FUT14";

    private const string SessionId = "FIFA14SERVERSESSION0000000000000";

    private const string FutBootXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<futData>\n" +
        "  <bootString>FUT14</bootString>\n" +
        "  <version>1</version>\n" +
        "  <minorVersion>0</minorVersion>\n" +
        "  <futNotAvailable>false</futNotAvailable>\n" +
        "  <enabled>true</enabled>\n" +
        "</futData>";

    private static long ParseLong(string s, long dflt) => long.TryParse(s, out var v) ? v : dflt;

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "...<truncated>";
}
