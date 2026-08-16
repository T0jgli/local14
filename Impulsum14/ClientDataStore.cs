using System.Text.Json;

namespace Impulsum14;

internal static class ClientDataStore
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "Profile", "clientdata.json");
    private static readonly Dictionary<string, string> _data = Load();

    public static string Get(string key)
    {
        lock (_lock) return _data.TryGetValue(key, out var v) ? v : "{}";
    }

    public static void Set(string key, string json)
    {
        lock (_lock)
        {
            _data[key] = string.IsNullOrWhiteSpace(json) ? "{}" : json;
            Save();
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _data.Clear();
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
            Console.WriteLine($"[ClientData] failed to load {_path}, using defaults: {ex.GetType().Name}: {ex.Message}");
        }
        return new Dictionary<string, string>();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientData] failed to save {_path}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
