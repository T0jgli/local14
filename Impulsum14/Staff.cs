namespace Impulsum14;

internal readonly record struct StaffCard(
    int ResourceId, string Name, int Rating, string ItemType, int CardSubType, int Rare,
    int Attr, int Amount);

internal static class Staff
{
    internal static readonly StaffCard[] All = Load();

    private static StaffCard[] Load()
    {
        if (Environment.GetEnvironmentVariable("FUT_SERVE_STAFF") == "0")
        {
            Console.WriteLine("[Staff] not served (FUT_SERVE_STAFF=0).");
            return System.Array.Empty<StaffCard>();
        }

        var path = Path.Combine(AppContext.BaseDirectory, "FUTDB", "staff.tsv");
        var list = new System.Collections.Generic.List<StaffCard>();
        try
        {
            foreach (var line in System.IO.File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c.Length < 8) continue;
                // itemType  cardsubtypeid  resourceId  rating  rare  attr  amount  name
                if (!int.TryParse(c[2], out int rid)) continue;   // skips the header row
                list.Add(new StaffCard(
                    ResourceId:  rid,
                    Name:        c[7],
                    Rating:      int.TryParse(c[3], out int rt)   ? rt   : 75,
                    ItemType:    c[0],
                    CardSubType: int.TryParse(c[1], out int sub)  ? sub  : 4,
                    Rare:        int.TryParse(c[4], out int rare) ? rare : 0,
                    Attr:        int.TryParse(c[5], out int at)   ? at   : 0,
                    Amount:      int.TryParse(c[6], out int amt)  ? amt  : 0));
            }
            Console.WriteLine($"[Staff] loaded {list.Count} from {path}");
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"[Staff] no {path}, running without extra staff");
        }
        catch (Exception ex) { Console.WriteLine($"[Staff] failed: {ex.Message}"); }
        return list.ToArray();
    }
}
