using System.IO;
using System.Text.Json;

namespace ImpulsumLauncher14.Models;

public class LauncherConfig
{
    public string GamePath { get; set; } = string.Empty;
    public string ServerPath { get; set; } = string.Empty;
    public bool AutoStartServer { get; set; }
    public string ServerCommitHash { get; set; } = string.Empty;

    private static readonly string ConfigPath = Path.Combine(
        AppContext.BaseDirectory, "launcher-config.json");

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
            }
        }
        catch { }
        return new LauncherConfig();
    }
}
