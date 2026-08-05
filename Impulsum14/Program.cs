using System.Net;
using System.Security.Cryptography.X509Certificates;
using Blaze.Core;
using EATDF.Serialization;
using Microsoft.Extensions.Logging;
using ProtoFire.Tls;

namespace Impulsum14;

internal static class Program
{
    private static int RedirPort => EnvInt("FIFA14_REDIR_PORT", 42127);
    private static int BlazePort => EnvInt("FIFA14_BLAZE_PORT", 10000);
    private static int WebPort => EnvInt("FIFA14_WEB_PORT", 9988);
    private static bool Fire2 => Environment.GetEnvironmentVariable("FIFA14_FIRE2") == "1";

    // FIFA 14's wire = Fire (v1) framing but Heat2 TDF encoding (confirmed by capturing
    // getServerInstance: integer type-byte=0x00, union=0x06 -> Heat2Type, and the 183-byte
    // body decoded cleanly). Heat2 is the default; set FIFA14_HEAT1=1 to force the old Heat1.

    private static bool Heat1 => Environment.GetEnvironmentVariable("FIFA14_HEAT1") == "1";

    private static async Task Main()
    {
        using var loggerFactory = LoggerFactory.Create(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
            .AddFilter(null, LogLevel.Debug));
        var log = loggerFactory.CreateLogger("Impulsum14");

        var cert = LoadCert(log);
        var protoCert = ProtoSSLCertificate.FromX509Certificate2(cert);
        var callbacks = new ServerCallbacks(protoCert, loggerFactory.CreateLogger("Blaze"));

        var enc = Fire2 ? FrameEncoding.Fire2 : FrameEncoding.Fire;
        TdfSerializer serializer = Heat1 ? new HeatSerializer() : new Heat2Serializer();
        log.LogInformation("Encoding: {0} framing + {1} TDF", enc, serializer.Name);

        var redirRouter = new BlazeRouter();
        redirRouter.AddComponent(new RedirectorComponent(BlazePort, loggerFactory.CreateLogger("Redirector")));
        var redirServer = new BlazeServer(MakeConfig(RedirPort, enc, serializer, redirRouter, protoCert, callbacks, secure: true),
            loggerFactory.CreateLogger<BlazeServer>());

        // OSDK web service
        var webBaseUrl = $"http://127.0.0.1:{WebPort}";
        var webServer = new WebServer(WebPort, loggerFactory.CreateLogger("Web"));

        // Main Blaze server
        var blazeRouter = new BlazeRouter();
        blazeRouter.AddComponent(new UtilComponent(loggerFactory.CreateLogger("Util"), webBaseUrl));
        blazeRouter.AddComponent<AuthenticationComponent>();
        blazeRouter.AddComponent<UserSessionsComponent>();
        blazeRouter.AddComponent<CensusDataComponent>();
        blazeRouter.AddComponent<AssociationListsComponent>();
        blazeRouter.AddComponent<MessagingComponent>();
        blazeRouter.AddComponent<RoomsComponent>();
        blazeRouter.AddComponent<StatsComponent>();
        blazeRouter.AddComponent<ClubsComponent>();
        blazeRouter.AddComponent(new SponsoredEventsComponent()); 
        blazeRouter.AddComponent(new OsdkSettingsComponent()); 
        var blazeServer = new BlazeServer(MakeConfig(BlazePort, enc, serializer, blazeRouter, protoCert, callbacks, secure: false),
            loggerFactory.CreateLogger<BlazeServer>());

        log.LogInformation("Impulsum14 starting: redirector :{0} -> blaze :{1}, web :{2}", RedirPort, BlazePort, WebPort);
        await Task.WhenAll(redirServer.StartAsync(), blazeServer.StartAsync(), webServer.StartAsync());
        await Task.Delay(Timeout.Infinite);
    }

    private static BlazeServerConfig MakeConfig(int port, FrameEncoding enc, TdfSerializer serializer,
        BlazeRouter router, ProtoSSLCertificate cert, ServerCallbacks callbacks, bool secure) => new()
    {
        LocalEndpoint = new IPEndPoint(IPAddress.Loopback, port),
        PacketFrameEncoding = enc,
        Serializer = serializer,
        Secure = secure,
        Certificate = secure ? cert : null,
        CallbackHandler = callbacks,
        Router = router,
    };

    private static X509Certificate2 LoadCert(ILogger log)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var pfx = Path.Combine(dir, "certs", "server.pfx");
            if (File.Exists(pfx))
            {
                var c = new X509Certificate2(pfx, (string)null,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                log.LogInformation("Loaded cert {0}", c.Subject);
                return c;
            }
            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }
        throw new FileNotFoundException("certs/server.pfx not found.");
    }

    private static int EnvInt(string name, int dflt) => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : dflt;
}
