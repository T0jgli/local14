using System.Text.Json;

namespace FIFAServer14;

internal static class UserSettingsStore
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "usersettings.json");
    private static readonly Dictionary<string, string> _data = Load();

    public static IReadOnlyDictionary<string, string> All
    {
        get { lock (_lock) return new Dictionary<string, string>(_data); }
    }

    public static string Get(string key)
    {
        lock (_lock) return _data.TryGetValue(key ?? "", out var v) ? v : "";
    }

    public static void Set(string key, string data)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_lock)
        {
            _data[key] = data ?? "";
            Save();
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                       ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserSettings] failed to load {_path}, starting fresh: {ex.GetType().Name}: {ex.Message}");
        }
        return new Dictionary<string, string>();
    }

    private static void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data)); }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserSettings] failed to save {_path}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
