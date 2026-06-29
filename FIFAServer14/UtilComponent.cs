using Blaze.Core;
using Blaze3SDK.Blaze;
using Blaze3SDK.Blaze.Util;
using Blaze3SDK.Components;
using EATDF.Types;
using Microsoft.Extensions.Logging;

namespace FIFAServer14;

internal sealed class UtilComponent : UtilComponentBase.Server
{
    private readonly ILogger _log;
    private const string GameIp = "127.0.0.1";
    private readonly string _web; // base URL

    // EA's real FIFA 14 asset CDN. Keep static content here until we mirror it locally.
    private const string CdnBase = "https://fifa17.content.easports.com/fifa/fltOnlineAssets/C74DDF38-0B11-49b0-B199-2E2A11D1CC13/2014";
    public UtilComponent(ILogger log, string webBaseUrl) { _log = log; _web = webBaseUrl.TrimEnd('/'); }

    private static uint Now() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public override Task<PreAuthResponse> PreAuthAsync(PreAuthRequest request, BlazeRpcContext context)
    {
        _log.LogInformation("preAuth received");
        return Task.FromResult(new PreAuthResponse
        {
            AuthenticationSource = "303107",
            RegistrationSource = "303107",
            ComponentIds = new List<ushort> { 1, 4, 5, 7, 9, 10, 11, 12, 13, 15, 21, 25, 28, 2049, 2050, 30722 },
            Config = new FetchConfigResponse
            {
                Config = new Dictionary<string, string>
                {
                    { "connIdleTimeout", "120s" },
                    { "defaultRequestTimeout", "80s" },
                    { "pingPeriod", "20s" },
                }
            },
            UnderageSupported = false,
            PersonaNamespace = "cem_ea_id",
            LegalDocGameIdentifier = "fifa-2014-pc",
            Platform = "pc",
            InstanceName = "fifa-2014-pc",
            QosSettings = new QosConfigInfo
            {
                NumLatencyProbes = 10,
                ServiceId = 0,
                BandwidthPingSiteInfo = new QosPingSiteInfo { Address = GameIp, Port = 17502, SiteName = "ea-sjc" },
                PingSiteInfoByAliasMap = new Dictionary<string, QosPingSiteInfo>
                {
                    { "ea-sjc", new QosPingSiteInfo { Address = GameIp, Port = 17502, SiteName = "ea-sjc" } },
                },
            },
            ServerVersion = "FIFA14 Blaze 13.3.0.5.0",
        });
    }

    public override Task<PingResponse> PingAsync(EmptyMessage request, BlazeRpcContext context)
        => Task.FromResult(new PingResponse { ServerTime = Now() });

    // Client loads user settings on the main-menu screen; return empty (no saved settings).
    public override Task<UserSettingsLoadAllResponse> UserSettingsLoadAllAsync(UserSettingsLoadAllRequest request, BlazeRpcContext context) => Task.FromResult(new UserSettingsLoadAllResponse { DataMap = new Dictionary<string, string>() });

    public override Task<UserSettingsResponse> UserSettingsLoadAsync(UserSettingsLoadRequest request, BlazeRpcContext context) => Task.FromResult(new UserSettingsResponse { Key = request.Key, Data = "" });

    public override Task<EmptyMessage> SetClientMetricsAsync(ClientMetrics request, BlazeRpcContext context) => Task.FromResult(new EmptyMessage());
    public override Task<LocalizeStringsResponse> LocalizeStringsAsync(LocalizeStringsRequest request, BlazeRpcContext context)
    {
        var map = new Dictionary<string, string>();
        foreach (var id in request.StringIds)
            map[id] = id;
        return Task.FromResult(new LocalizeStringsResponse { LocalizedStrings = map });
    }

    public override Task<EmptyMessage> SetClientDataAsync(ClientData request, BlazeRpcContext context) => Task.FromResult(new EmptyMessage());

    public override Task<FetchConfigResponse> FetchClientConfigAsync(FetchClientConfigRequest request, BlazeRpcContext context)
    {
        _log.LogInformation("fetchClientConfig section='{0}'", request.ConfigSection);

        var cfg = new Dictionary<string, string>();

        cfg["OSDK_PEERBUFFERSIZE"]             = "16384";
        cfg["OSDK_DISTBUFFERSIZE_IN"]          = "16384";
        cfg["OSDK_DISTBUFFERSIZE_OUT"]         = "16384";
        cfg["OSDK_MAXGAMES"]                   = "16";
        cfg["OSDK_MAXROOMS"]                   = "16";
        cfg["OSDK_USERROOM_PREFIX"]            = "room";
        cfg["OSDK_MATCHUP_TIMEOUT"]            = "30";
        cfg["OSDK_KEEPALIVEINTERVAL"]          = "30";
        cfg["OSDK_STATS_EMPTY_CELL"]           = "-1";
        cfg["OSDK_TICKER_COUNT"]               = "10";
        cfg["JOIN_GAME_TIMEOUT"]               = "30";
        cfg["OSDK_USERLIST_REQUEST_MAX_USERS"] = "50";
        cfg["POW_MDL_MAX_IMAGESIZE"]           = "1048576";
        cfg["POW_MDL_DELAYNEWSDOWNLOAD"]       = "0";
        cfg["LIVE_CONTENT_HOST"]               = $"{CdnBase}/";
        cfg["ROSTERUPDATE_URL"]                = $"{CdnBase}/rosterupdate";
        cfg["ROSTER_URL"]                      = $"{CdnBase}/roster";
        cfg["EASW/ENABLED"]                    = "1";
        cfg["OSDK_EASW_REQ_URL"]               = $"{_web}/easw/req";
        cfg["OSDK_EASW_AUTH_URL"]              = $"{_web}/easw/auth";
        cfg["OSDK_EASW_EVENT_URL"]             = $"{_web}/easw/event";
        cfg["OSDK_EASW_MEDIA_URL"]             = $"{_web}/easw/media";
        cfg["OSDK_EASW_GF_FILE_URL"]           = $"{_web}/easw/gf";
        cfg["OSDK_EASW_ALLOWED_LOCALES"]       = "en_US,en_GB,en_US.UTF-8,enUS,enGB,fr_FR,de_DE,es_ES,it_IT,pt_BR,nl_NL";
        cfg["OSDK_EASW_CONNECT_RETRY_PERIOD"]  = "30";
        cfg["CMS_BASE_URL"]                    = $"{_web}/cms";
        cfg["CMS_APIKEY"]                      = "fifa14";
        cfg["CMS_SKUID"]                       = "FFA14PCC";
        cfg["FUT_URI"]                         = $"{_web}/fut";
        cfg["FUT_RS4_BASE_URL"]                = $"{_web}/fut/rs4";
        cfg["FUT/ROSTERUPDATE_URL"]            = $"{_web}/fut/rosterupdate";
        cfg["FUTDYNAMICMESSAGES_URL_BASE"]     = $"{_web}/fut/dynamicmessages";
        cfg["FUTBOOTCFGFILE_URL"]              = $"{_web}/fut/";
        cfg["ONLINE/SERVER_RS4"]               = $"{_web}";
        cfg["FIFA_RS4_URL"]                    = $"{_web}";
        cfg["FIFA_RS4_TIMEOUT"]                = "30";
        cfg["FIFALEADERBOARD_BASE_URL"]        = $"{_web}/leaderboard";
        cfg["ROUTINGCFGFILE_URL"]              = $"{_web}/dime/dimerouting.xml";

        return Task.FromResult(new FetchConfigResponse { Config = cfg });
    }

    public override Task<GetTelemetryServerResponse> GetTelemetryServerAsync(GetTelemetryServerRequest request, BlazeRpcContext context)
        => Task.FromResult(Tele());

    public override Task<PostAuthResponse> PostAuthAsync(EmptyMessage request, BlazeRpcContext context)
    {
        _log.LogInformation("postAuth received");
        return Task.FromResult(new PostAuthResponse
        {
            TelemetryServer = Tele(),
            TickerServer = new GetTickerServerResponse { Address = GameIp, Port = 6776, Key = "key" },
            UserOptions = new UserOptions { TelemetryOpt = TelemetryOpt.TELEMETRY_OPT_OUT },
        });
    }

    private static GetTelemetryServerResponse Tele() => new()
    {
        Address = GameIp,
        Disable = "disa",
        Filter = "filt",
        IsAnonymous = false,
        Key = "key",
        Locale = 1701729619,
        NoToggleOk = "nook",
        Port = 6767,
        SendDelay = 10,
        SendPercentage = 10,
        SessionID = "id",
        UseServerTime = "0",
    };
}
