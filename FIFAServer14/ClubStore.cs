using System.Text.Json;

namespace FIFAServer14;

internal sealed class ClubData
{
    public List<ClubItem> Inventory { get; set; } = new();
    public List<Squad> Squads { get; set; } = new();
    public int ActiveSquadId { get; set; } = 0;
    public bool Seeded { get; set; } = false;
}

internal static class ClubStore
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "club_data.json");
    private static readonly ClubData _data = Load();

    static ClubStore()
    {
        if (!_data.Seeded)
            Seed();
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
            var seedRnd = new Random();
            string[] starterPositions = { "GK", "RB", "CB", "CB", "LB", "RM", "CM", "CM", "LM", "ST", "ST" };
            var seedUsed = new HashSet<int>();
            var defaultSquad = new Squad { Id = 0, Name = "FUT14 FC", Formation = "f442" };
            for (int i = 0; i < starterPositions.Length; i++)
            {
                var pool = RealPlayers.All.Where(p => p.Position == starterPositions[i] && !seedUsed.Contains(p.Id)).ToArray();
                if (pool.Length == 0)
                    pool = RealPlayers.All.Where(p => !seedUsed.Contains(p.Id)).ToArray();
                RealPlayer chosen = pool[seedRnd.Next(pool.Length)];
                seedUsed.Add(chosen.Id);
                long itemId = 900000 + i;
                _data.Inventory.Add(new ClubItem(itemId, chosen, 7));
                defaultSquad.Slots[i] = itemId;
            }
            long extraItemId = 800100;
            foreach (var p in RealPlayers.All)
            {
                if (seedUsed.Contains(p.Id)) continue;
                _data.Inventory.Add(new ClubItem(extraItemId++, p, 6));
            }
            _data.Squads.Add(defaultSquad);
            _data.ActiveSquadId = 0;
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
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Club] failed to save {_path}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
