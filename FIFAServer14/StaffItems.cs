namespace FIFAServer14;

internal readonly record struct StaffItem(
    long ItemId, string ItemType, long AssetId, int Rating, int Rare, int Attr, int Amount,
    int Pos, int PosBonus, int Nation, int Formation, int Talk, string Name);

internal static class StaffItems
{
    internal const long StaffItemIdBase = 970_000_000L;

    internal static readonly StaffItem[] Catalog = Load();

    private static StaffItem[] Load()
    {
        if (Environment.GetEnvironmentVariable("FUT_SERVE_STAFF") != "1")
        {
            Console.WriteLine("[Staff] parked (empty tab); data preserved in staff.tsv. Set FUT_SERVE_STAFF=1 to serve.");
            return Array.Empty<StaffItem>();
        }

        string path = Path.Combine(AppContext.BaseDirectory, "staff.tsv");
        var list = new List<StaffItem>();
        try
        {
            long id = StaffItemIdBase;
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] c = line.Split('\t');
                if (c.Length < 12) continue;
                // itemType  assetId  rating  rare  attr  amount  pos  posbonus  nation  formation  talk  name
                int F(int i) => c.Length > i && c[i].Length > 0 && int.TryParse(c[i], out int v) ? v : 0;
                list.Add(new StaffItem(id++, c[0].Trim(), long.Parse(c[1]),
                    F(2), F(3), F(4), F(5), F(6), F(7), F(8), F(9), F(10), c[11]));
            }
            Console.WriteLine($"[Staff] loaded {list.Count} from {path}");
        }
        catch (FileNotFoundException) { Console.WriteLine($"[Staff] no {path}"); }
        catch (Exception ex) { Console.WriteLine($"[Staff] FAILED {path}: {ex.GetType().Name}: {ex.Message}"); }
        return list.ToArray();
    }

    public static string BuildJson(StaffItem it, long timestamp)
    {
        bool mgr = it.ItemType == "staffManager";
        var sb = new System.Text.StringBuilder(256);
        sb.Append("{\"id\":").Append(it.ItemId)
          .Append(",\"timestamp\":").Append(timestamp)
          .Append(",\"untradeable\":false")
          .Append(",\"assetId\":").Append(it.AssetId)
          .Append(",\"resourceId\":").Append(it.AssetId)
          .Append(",\"itemType\":\"").Append(it.ItemType).Append('"')
          .Append(",\"rating\":").Append(it.Rating)
          .Append(",\"rareflag\":").Append(it.Rare)
          .Append(",\"owners\":1,\"discardValue\":0,\"itemState\":\"free\"")
          .Append(",\"cardsubtypeid\":0,\"lastSalePrice\":0")
          .Append(",\"statsList\":[],\"lifetimeStats\":[],\"attributeList\":[]")
          .Append(",\"teamid\":0,\"pile\":6,\"preferredPosition\":\"\"")
          .Append(",\"name\":\"").Append(Esc(it.Name)).Append('"');
        if (mgr)
            sb.Append(",\"nation\":").Append(it.Nation)
              .Append(",\"nationId\":").Append(it.Nation)
              .Append(",\"leagueId\":0")
              .Append(",\"managerTalk\":").Append(it.Talk);
        sb.Append('}');
        return sb.ToString();
    }

    private static string Esc(string s) => (s ?? "")
        .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
}
