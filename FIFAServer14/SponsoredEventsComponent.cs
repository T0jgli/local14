using Blaze.Core;
using EATDF;
using EATDF.Members;
using EATDF.Types;

namespace FIFAServer14;

public sealed class URLResponse : Tdf
{
    private static readonly TdfMemberInfo[] __typeInfos =
    [
        new TdfMemberInfo("URL", "mURL", 0xD72B0000, TdfType.String, 0, true), // URL
    ];

    private readonly ITdfMember[] __members;
    private readonly TdfString _url = new(__typeInfos[0]);

    public URLResponse() { __members = [_url]; }

    public override Tdf CreateNew() => new URLResponse();
    public override ITdfMember[] GetMembers() => __members;
    public override TdfMemberInfo[] GetMemberInfos() => __typeInfos;
    public override string GetClassName() => "URLResponse";
    public override string GetFullClassName() => "Blaze::SponsoredEvents::URLResponse";

    public string URL { get => _url.Value; set => _url.Value = value; }
}

internal sealed class SponsoredEventsComponent : BlazeComponent
{
    private static readonly HttpClient _http = new();
    
    public override ushort Id => 2076;
    public override string Name => "SponsoredEventsComponent";
    public override string GetErrorName(ushort errorCode) => $"0x{errorCode:X4}";

    public SponsoredEventsComponent()
    {
        RegisterCommand(new RpcCommandFunc<EmptyMessage, URLResponse, EmptyMessage>
        {
            Id = 3, // getEventsURL
            Name = "getEventsURL",
            IsSupported = true,
            Func = async (req, ctx) =>
            {
                Console.WriteLine("[SponsoredEvents] getEventsURL -> returning events URL");
                
                // This is temp till we switch to a local cdn 
                var url = "https://fifa17.content.easports.com/fifa/fltOnlineAssets/C74DDF38-0B11-49b0-B199-2E2A11D1CC13/2014/fifa/sponsoredevents/events_list.xml";
                var localPath = Path.Combine("cached_content", "fifa", "sponsoredevents", "events_list.xml");
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!File.Exists(localPath))
                        {
                            Console.WriteLine($"[SponsoredEvents] Downloading {url}...");
                            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                            
                            var content = await _http.GetStringAsync(url);
                            await File.WriteAllTextAsync(localPath, content);
                            
                            Console.WriteLine($"[SponsoredEvents] Cached to {localPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SponsoredEvents] Failed to cache: {ex.Message}");
                    }
                });
                
                return await Task.FromResult<Tdf>(new URLResponse
                {
                    URL = "fifa/sponsoredevents/events_list.xml"
                });
            },
        });
    }
}
