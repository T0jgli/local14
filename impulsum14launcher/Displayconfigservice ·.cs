using System.Globalization;
using System.IO;

namespace ImpulsumLauncher14.Services;

public class DisplaySettings
{
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public bool FullScreen { get; set; } = true;

    public int RenderingQuality { get; set; } = 1;

    public int WaitForVsync { get; set; } = 2;

    public int MsaaLevel { get; set; }

    public bool ScreenSleep { get; set; }
    public bool DisableWindowsAero { get; set; }
    public bool VoiceChat { get; set; }

    public int ControllerDefault { get; set; }
}

public class DisplayConfigService
{
    private const string FolderName = "FIFA 14";
    private const string FileName = "fifasetup.ini";

    public static string GetIniPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(documents, FolderName, FileName);
    }

    public bool Exists() => File.Exists(GetIniPath());

    public bool TryGet(out DisplaySettings settings)
    {
        settings = new DisplaySettings();
        var path = GetIniPath();
        if (!File.Exists(path))
            return false;

        var values = ReadAll(path);

        if (values.TryGetValue("RESOLUTIONWIDTH", out var w) && int.TryParse(w, out var width))
            settings.ResolutionWidth = width;
        if (values.TryGetValue("RESOLUTIONHEIGHT", out var h) && int.TryParse(h, out var height))
            settings.ResolutionHeight = height;
        if (values.TryGetValue("FULLSCREEN", out var fs) && int.TryParse(fs, out var fullScreen))
            settings.FullScreen = fullScreen != 0;
        if (values.TryGetValue("RENDERINGQUALITY", out var rq) && int.TryParse(rq, out var quality))
            settings.RenderingQuality = Math.Clamp(quality, 0, 3);
        if (values.TryGetValue("WAITFORVSYNC", out var vs) && int.TryParse(vs, out var vsync))
            settings.WaitForVsync = vsync;
        if (values.TryGetValue("MSAA_LEVEL", out var msaa) && int.TryParse(msaa, out var msaaLevel))
            settings.MsaaLevel = msaaLevel;
        if (values.TryGetValue("SCREEN_SLEEP", out var sleep) && int.TryParse(sleep, out var sleepVal))
            settings.ScreenSleep = sleepVal != 0;
        if (values.TryGetValue("DISABLE_WINDAERO", out var aero) && int.TryParse(aero, out var aeroVal))
            settings.DisableWindowsAero = aeroVal != 0;
        if (values.TryGetValue("VOICECHAT", out var vc) && int.TryParse(vc, out var vcVal))
            settings.VoiceChat = vcVal != 0;
        if (values.TryGetValue("CONTROLLER_DEFAULT", out var cd) && int.TryParse(cd, out var cdVal))
            settings.ControllerDefault = cdVal;

        return true;
    }

    public bool TryUpdate(DisplaySettings settings)
    {
        try
        {
            var path = GetIniPath();
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var values = File.Exists(path) ? ReadAll(path) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var aspectRatio = settings.ResolutionHeight != 0
                ? (double)settings.ResolutionWidth / settings.ResolutionHeight
                : 1.7777778;

            values["ASPECTRATIO"] = aspectRatio.ToString("G9", CultureInfo.InvariantCulture);
            values["RESOLUTIONWIDTH"] = settings.ResolutionWidth.ToString();
            values["RESOLUTIONHEIGHT"] = settings.ResolutionHeight.ToString();
            values["FULLSCREEN"] = settings.FullScreen ? "1" : "0";
            values["RENDERINGQUALITY"] = settings.RenderingQuality.ToString();
            values["WAITFORVSYNC"] = settings.WaitForVsync.ToString();
            values["MSAA_LEVEL"] = settings.MsaaLevel.ToString();
            values["SCREEN_SLEEP"] = settings.ScreenSleep ? "1" : "0";
            values["DISABLE_WINDAERO"] = settings.DisableWindowsAero ? "1" : "0";
            values["VOICECHAT"] = settings.VoiceChat ? "1" : "0";
            values["CONTROLLER_DEFAULT"] = settings.ControllerDefault.ToString();

            var lines = values
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key} = {kv.Value}")
                .ToArray();

            File.WriteAllLines(path, lines);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool EnsureDefaultsExist()
    {
        if (Exists()) return true;
        return TryUpdate(new DisplaySettings());
    }


    private static Dictionary<string, string> ReadAll(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}