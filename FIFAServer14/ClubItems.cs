namespace FIFAServer14;

internal readonly record struct CosmeticItem(
    long ItemId, string Type, int AssetId, long ResourceId, int SubType, string Name, int Rating);

internal static class ClubItems
{
    private const long BallBase    = 8120000;
    private const long StadiumBase = 6200000;
    private const long KitBase     = 6300000;
    private const long BadgeBase   = 6000000;

    internal const long CosmeticItemIdBase = 950_000_000L;   // clear of players (id) and specials (900M)

    private static readonly (string Type, int SubType, long Base)[] TypeInfo =
    {
        ("ball",    30, BallBase),
        ("stadium", 10, StadiumBase),
        ("kit",      9, KitBase),
        ("badge",   11, BadgeBase),
    };

    internal static readonly CosmeticItem[] Catalog = Load();

    private static (int SubType, long Base) Info(string type)
    {
        foreach (var t in TypeInfo) if (t.Type == type) return (t.SubType, t.Base);
        return (0, 0);
    }

    private static CosmeticItem[] Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "items.tsv");
        var list = new List<CosmeticItem>();
        try
        {
            long id = CosmeticItemIdBase;
            int lineNo = 1;
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                lineNo++;
                if (line.Length == 0 || line[0] == '#') continue;
                string[] c = line.Split('\t');
                if (c.Length < 3) continue;
                // type  assetId  name  [resourceId]  [rating]
                string type = c[0].Trim();
                var (subType, baseRes) = Info(type);
                if (subType == 0)
                {
                    Console.WriteLine($"[Items] {path}:{lineNo} unknown type '{type}', skipping");
                    continue;
                }
                int assetId = int.Parse(c[1]);
                string name = c.Length > 2 ? c[2] : type;
                long resId = c.Length > 3 && c[3].Length > 0 && c[3] != "-" ? long.Parse(c[3]) : baseRes + assetId;
                int rating = c.Length > 4 && c[4].Length > 0 ? int.Parse(c[4]) : 75;
                list.Add(new CosmeticItem(id++, type, assetId, resId, subType, name, rating));
            }
            Console.WriteLine($"[Items] loaded {list.Count} club items from {path}");
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"[Items] no {path}, running without club items");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Items] FAILED to load {path}: {ex.GetType().Name}: {ex.Message}");
        }
        return list.Concat(Probe(list.Count)).ToArray();
    }

    private static IEnumerable<CosmeticItem> Probe(int startCount)
    {
        string spec = Environment.GetEnvironmentVariable("FUT_PROBE_ITEMS");
        if (string.IsNullOrWhiteSpace(spec)) yield break;

        long id = CosmeticItemIdBase + 500_000 + startCount;   // clear of the real catalog ids
        foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split(':');
            if (kv.Length != 2) continue;
            string type = kv[0].Trim();
            var (subType, baseRes) = Info(type);
            if (subType == 0) { Console.WriteLine($"[Items] probe: unknown type '{type}'"); continue; }

            string[] range = kv[1].Split('-');
            if (!int.TryParse(range[0], out int from)) continue;
            int to = range.Length > 1 && int.TryParse(range[1], out int t) ? t : from;
            Console.WriteLine($"[Items] probing {type} assetId {from}..{to}");
            for (int a = from; a <= to; a++)
                yield return new CosmeticItem(id++, type, a, baseRes + a, subType, $"{type} {a}", 75);
        }
    }

    public static string BuildJson(CosmeticItem it, long timestamp)
    {
        int discard = it.Rating;
        string head =
            "{\"id\":" + it.ItemId + ",\"timestamp\":" + timestamp + ",\"formation\":\"f442\"," +
            "\"untradeable\":false,\"assetId\":" + it.AssetId + ",\"rating\":" + it.Rating + "," +
            "\"itemType\":\"" + (it.Type == "badge" ? "custom" : it.Type) + "\"," +
            "\"resourceId\":" + it.ResourceId + ",\"owners\":1,\"discardValue\":" + discard + "," +
            "\"itemState\":\"free\",\"cardsubtypeid\":" + it.SubType + ",\"lastSalePrice\":0," +
            "\"statsList\":[],\"lifetimeStats\":[],\"attributeList\":[],\"teamid\":0,\"rareflag\":0," +
            "\"leagueId\":0,\"pile\":6,\"resourceGameYear\":2014";

        string name = Esc(it.Name);
        string tail = it.Type switch
        {
            "stadium" => ",\"cardassetid\":" + it.AssetId + ",\"category\":4,\"name\":\"" + name +
                         "\",\"description\":\"StadiumDesc_Server\",\"biodescription\":\"StadiumDetailDesc\"," +
                         "\"stadiumid\":" + it.AssetId + ",\"capacity\":30000}",
            "ball"    => ",\"cardassetid\":" + it.AssetId + ",\"category\":1,\"name\":\"" + name +
                         "\",\"value\":" + it.Rating + ",\"manufacturer\":\"ManufacturerGeneric\"}",
            "kit"     => ",\"category\":2,\"year\":0}",
            "badge"   => ",\"category\":1,\"value\":" + it.Rating + ",\"weightrare\":0,\"header\":\"Badge\"}",
            _         => "}",
        };
        return head + tail;
    }

    private static string Esc(string s) => (s ?? "")
        .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
}
