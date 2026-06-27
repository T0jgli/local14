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
    public UtilComponent(ILogger log) { _log = log; }

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

        if (request.ConfigSection == "OSDK_CORE")
        {
            cfg["LIVE_CONTENT_HOST"]               = "https://fifa17.content.easports.com/fifa/fltOnlineAssets/C74DDF38-0B11-49b0-B199-2E2A11D1CC13/2014/";
            cfg["ROSTERUPDATE_URL"]                = "https://fifa17.content.easports.com/fifa/fltOnlineAssets/C74DDF38-0B11-49b0-B199-2E2A11D1CC13/2014/rosterupdate";
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
        }

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
