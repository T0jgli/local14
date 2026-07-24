using System.Text.Json;

namespace FIFAServer14;

internal sealed class ClubData
{
    public List<ClubItem> Inventory { get; set; } = new();
    public List<CosmeticItem> Cosmetics { get; set; } = new();
    public List<ConsumableItem> Consumables { get; set; } = new();
    public List<StaffItem> Staff { get; set; } = new();
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

    private const long SpecialItemIdBase = 900_000_000L;

    static ClubStore()
    {
        if (!_data.AllPlayersSeeded)
            SeedWholeDatabase();
        else if (!_data.Seeded)
            Seed();

        SeedSpecials();
        SeedCosmetics();
        SeedConsumables();
        SeedStaff();
    }

    private static void SeedStaff()
    {
        lock (_lock)
        {
            _data.Staff.Clear();
            _data.Staff.AddRange(StaffItems.Catalog);
            if (StaffItems.Catalog.Length > 0)
                Console.WriteLine($"[Club] staff: {StaffItems.Catalog.Length}");
            Save();
        }
    }

    private static void SeedConsumables()
    {
        lock (_lock)
        {
            var catalog = ConsumableItems.Catalog;
            _data.Consumables.Clear();
            _data.Consumables.AddRange(catalog);
            if (catalog.Length > 0)
                Console.WriteLine($"[Club] consumables: {catalog.Length}");
            Save();
        }
    }

    private static void SeedCosmetics()
    {
        lock (_lock)
        {
            var catalog = ClubItems.Catalog;
            _data.Cosmetics.Clear();
            _data.Cosmetics.AddRange(catalog);
            if (catalog.Length > 0)
                Console.WriteLine($"[Club] club items: {catalog.Length}");
            Save();
        }
    }

    private static void SeedSpecials()
    {
        lock (_lock)
        {
            var specials = SpecialCards.All;
            int before = _data.Inventory.RemoveAll(c => c.ItemId >= SpecialItemIdBase);
            for (int i = 0; i < specials.Length; i++)
                _data.Inventory.Add(new ClubItem(SpecialItemIdBase + i, specials[i], 6));   // 6 = club

            var live = new HashSet<long>(_data.Inventory.Select(c => c.ItemId));
            foreach (var squad in _data.Squads)
                foreach (int idx in squad.Slots.Where(s => !live.Contains(s.Value)).Select(s => s.Key).ToList())
                    squad.Slots.Remove(idx);

            if (before > 0 || specials.Length > 0)
                Console.WriteLine($"[Club] special cards: removed {before}, added {specials.Length}");
            Save();
        }
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
            var inXi = new HashSet<int>();
            void Pick(Func<RealPlayer, bool> where, int n)
            {
                foreach (var p in all.Where(p => !inXi.Contains(p.Id) && where(p))
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
