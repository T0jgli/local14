using System.Text.Json;

namespace FIFAServer14;

internal sealed class ClubData
{
    public List<ClubItem> Inventory { get; set; } = new();
    public List<Squad> Squads { get; set; } = new();
    public int ActiveSquadId { get; set; } = 0;
    public bool Seeded { get; set; } = false;
    public bool AllPlayersSeeded { get; set; } = false; 
}

internal static class ClubStore
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "club_data.json");
    private static readonly ClubData _data = Load();

    static ClubStore()
    {
        if (!_data.AllPlayersSeeded)
            SeedWholeDatabase();
        else if (!_data.Seeded)
            Seed();
    }

    private static void SeedWholeDatabase()
    {
        lock (_lock)
        {
            _data.Inventory.Clear();
            _data.Squads.Clear();

            var all = RealPlayers.All;
            var defPos = new HashSet<string> { "CB", "RB", "LB", "RWB", "LWB" };
            var midPos = new HashSet<string> { "CM", "CDM", "CAM", "RM", "LM" };
            var attPos = new HashSet<string> { "ST", "CF", "RW", "LW", "RF", "LF" };
            var xi = new List<RealPlayer>();
                                     .OrderByDescending(p => p.Rating).Take(n))
                { xi.Add(p); inXi.Add(p.Id); }
            }
            Pick(p => p.Position == "GK", 1);
            Pick(p => defPos.Contains(p.Position), 4);
            Pick(p => midPos.Contains(p.Position), 4);
            Pick(p => attPos.Contains(p.Position), 2);
            Pick(_ => true, 11 - xi.Count);   // top up if any category came short

            foreach (var p in all)
                _data.Inventory.Add(new ClubItem(p.Id, p, inXi.Contains(p.Id) ? 7 : 6));   // 7 = in squad, 6 = club

            if (xi.Count > 0)
            {
                var squad = new Squad { Id = 0, Name = "FUT14 FC", Formation = "f442" };
                for (int i = 0; i < xi.Count; i++) squad.Slots[i] = xi[i].Id;   // slot 0 = GK, then def/mid/att
                _data.Squads.Add(squad);
            }
            _data.ActiveSquadId = 0;
            _data.Seeded = true;
            _data.AllPlayersSeeded = true;
            Save();
            Console.WriteLine($"[Club] seeded {_data.Inventory.Count} players; starting XI = {xi.Count}");
        }
    }

    public static ClubData Get()
    {
        lock (_lock) return _data;
    }

    public static void Mutate(Action<ClubData> change)
    {
        lock (_lock)
        {
            change(_data);
            Save();
        }
    }

    private static void Seed()
    {
        lock (_lock)
        {
            _data.Seeded = true;
            Save();
        }
    }

    private static ClubData Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<ClubData>(File.ReadAllText(_path)) ?? new ClubData();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Club] failed to load {_path}, using defaults: {ex.GetType().Name}: {ex.Message}");
        }
        return new ClubData();
    }

    private static void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = false })); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Club] failed to save {_path}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
