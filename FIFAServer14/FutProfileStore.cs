using System.Text.Json;

namespace FIFAServer14;

internal sealed class FutClub
{
    public bool Established { get; set; } = false;   // false => new player, no club yet
    public long TeamId { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Abbr { get; set; } = "";
    public int BadgeId { get; set; } = 0;
    public int StadiumId { get; set; } = 0;
    public int KitId { get; set; } = 0;
}

internal sealed class FutProfile
{
    public long NucleusId { get; set; } = 1000;
    public string PersonaName { get; set; } = "FUT14";
    public bool IsReturningUser { get; set; } = false;   // new player by default
    public long Coins { get; set; } = 10000000;
    public long FifaPoints { get; set; } = 0;
    public FutClub Club { get; set; } = new();
}

internal static class FutProfileStore
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "fut_profile.json");
    private static readonly FutProfile _profile = Load();

    public static FutProfile Get()
    {
        lock (_lock) return _profile;
    }

    public static void Mutate(Action<FutProfile> change)
    {
        lock (_lock)
        {
            change(_profile);
            Save();
        }
    }

    public static void Reset()
    {
        lock (_lock)
        {
            _profile.IsReturningUser = false;
            _profile.Coins = 0;
            _profile.FifaPoints = 0;
            _profile.Club = new FutClub();
            Save();
        }
    }

    private static FutProfile Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<FutProfile>(File.ReadAllText(_path)) ?? new FutProfile();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FutProfile] failed to load {_path}, using defaults: {ex.GetType().Name}: {ex.Message}");
        }
        return new FutProfile();
    }

    private static void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex)
        {
            Console.WriteLine($"[FutProfile] failed to save {_path}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
