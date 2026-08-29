namespace Impulsum14;
internal static class Seasons
{
    private const int Rounds = 10;   // a season is 10 matches against a fixed fixture list
    private const int TopDivision = 1;
    private const int BottomDivision = 10;
    private const int OnlineIdBase = 100;

    internal static string ListJson(string type)
    {
        bool online = IsOnline(type);
        bool offlineOnly = string.Equals(type, "offline", System.StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        int n = 0;
        if (!online) n += AppendLadder(sb, online: false, idBase: 0);
        if (!offlineOnly) n += AppendLadder(sb, online: true, idBase: OnlineIdBase);
        string arr = sb.ToString();
        return "{\"seasons\":[" + arr + "],\"seasonData\":[" + arr + "],\"season\":[" + arr + "],\"totalResults\":" + n + "}";
    }

    private static bool IsOnline(string type) =>
        string.Equals(type, "online", System.StringComparison.OrdinalIgnoreCase);

    private static readonly (int titlePts, int promoPts, int relPts,
                             int titleCoins, int promoCoins, int holdCoins)[] DivRewards =
    {
        default,                              // [0] unused
        (28, 15, 15, 5600, 5300, 2800),       // Division 1  (top)
        (26, 23, 14, 5500, 5800, 3500),       // Division 2
        (26, 23, 15, 5600, 5300, 2800),       // Division 3
        (24, 21, 14, 4600, 3700, 2500),       // Division 4
        (22, 19, 13, 4100, 3300, 2300),       // Division 5
        (20, 17, 12, 3500, 3100, 2300),       // Division 6
        (18, 15, 10, 3500, 2800, 2100),       // Division 7
        (16, 13,  9, 2900, 2500, 1900),       // Division 8
        (14, 11,  7, 2100, 1700, 1300),       // Division 9
        (12,  9,  6, 1900, 1500,  300),       // Division 10 (bottom)
    };

    private static int AppendLadder(System.Text.StringBuilder sb, bool online, int idBase)
    {
        string typeWire = online ? "ONLINE" : "OFFLINE";
        int count = 0;

        for (int div = BottomDivision; div >= TopDivision; div--)
        {
            var d = DivRewards[div];
            int id = idBase + div;
            int title = div == TopDivision ? 1 : 0;
            // Season Difficulty stars scale with the division: bottom (10) = 1, top (1) = 5.
            int difficulty = System.Math.Clamp(6 - (div + 1) / 2, 1, 5);

            if (sb.Length > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(id).Append(",\"seasonId\":").Append(id)
              .Append(",\"divisionId\":").Append(div).Append(",\"divisionOffline\":").Append(div)
              .Append(",\"divisionOnline\":").Append(div).Append(",\"leagueId\":").Append(div)
              .Append(",\"name\":\"").Append(online ? "Online " : "").Append("Division ").Append(div)
              .Append("\",\"title\":").Append(title).Append(",\"type\":\"").Append(typeWire).Append("\",\"active\":true")
              .Append(",\"tournamentId\":").Append(8202000 + id).Append(",\"tournamentType\":0")
              .Append(",\"trophyResourceId\":").Append(8202000 + id)
              .Append(",\"numMatches\":").Append(Rounds).Append(",\"numRounds\":").Append(Rounds);

            sb.Append(",\"matches\":[");
            for (int r = 1; r <= Rounds; r++)
            {
                if (r > 1) sb.Append(',');
                sb.Append("{\"matchId\":").Append(r).Append(",\"round\":").Append(r)
                  .Append(",\"matchDifficulty\":").Append(difficulty).Append('}');
            }
            sb.Append("]");

            sb.Append(",\"prizeSet\":[");
            AppendPrize(sb, "CHAMPIONSHIP", d.titlePts, d.titleCoins, first: true);
            AppendPrize(sb, "PROMOTION",    d.promoPts, d.promoCoins, first: false);
            AppendPrize(sb, "MAINTENANCE",  d.relPts,   d.holdCoins,  first: false);
            sb.Append("]}");
            count++;
        }
        return count;
    }

    private static void AppendPrize(System.Text.StringBuilder sb, string level, int threshold, int coins, bool first)
    {
        if (!first) sb.Append(',');
        sb.Append("{\"prizeLevel\":\"").Append(level).Append("\",\"thresholdPoint\":").Append(threshold)
          .Append(",\"awardMappings\":[{\"timesWon\":1,\"awards\":[{\"type\":\"coin\",\"value\":").Append(coins)
          .Append(",\"count\":1}]}]}");
    }

    // The client is the sole authority on season progress, but it saves only the mutable half
    // (round + blobs) and expects the server to hand the season's identity back with it. Without
    // seasonId/divisionId on the GET the hub can't bind the save to a ladder entry and the game
    // hard-crashes on the way back from a match.
    internal static string UserJson(FutProfile p, string type)
    {
        bool online = IsOnline(type);
        string doc = online ? p.SeasonUserOnline : p.SeasonUserOffline;
        int div = System.Math.Clamp(online ? p.OnlineDivision : p.OfflineDivision, TopDivision, BottomDivision);

        if (doc.Length == 0)
            return "{\"divisionId\":" + div + ",\"round\":1,\"seasonId\":-1}";

        var identity = new System.Text.StringBuilder();
        Add(identity, doc, "seasonId", ((online ? OnlineIdBase : 0) + div).ToString());
        Add(identity, doc, "divisionId", div.ToString());
        Add(identity, doc, "leagueId", div.ToString());
        Add(identity, doc, "type", "\"" + (online ? "ONLINE" : "OFFLINE") + "\"");
        if (identity.Length == 0) return doc;

        string rest = doc[(doc.IndexOf('{') + 1)..].TrimStart();
        if (rest.StartsWith("}")) identity.Length--;   // client sent an empty object - no trailing comma
        return "{" + identity + rest;

        static void Add(System.Text.StringBuilder sb, string doc, string key, string value)
        {
            if (doc.Contains("\"" + key + "\"", System.StringComparison.Ordinal)) return;
            sb.Append('"').Append(key).Append("\":").Append(value).Append(',');
        }
    }

    internal static string TrophyJson(int entryId)
    {
        bool online = entryId >= 100;
        int wireDiv = online ? entryId - 100 : entryId;
        int realDiv = System.Math.Clamp(wireDiv, 1, 10);
        int design = 1100 + (10 - realDiv);   // Division 10 -> 1100 (bottom) .. Division 1 -> 1109 (top)
        string label = (online ? "Online Division " : "Division ") + realDiv;
        int trophyId = 8202000 + entryId;
        return "{\"tournamentId\":" + trophyId + ",\"tournamentType\":0,\"assetName\":\"trophy_" + design +
               "_gold\",\"silName\":\"trophy_" + design + "_dark\",\"locString\":[{\"lang\":\"ENG_US\",\"label\":\"" +
               label + "\"}]}";
    }

    internal static string HistoryJson() => "{\"seasons\":[],\"totalResults\":0}";

    internal static string ResetJson(int div) => "{\"offlineDivision\":" + div + "}";

    internal static void CaptureSave(FutProfile p, string body, string type)
    {
        string doc = (body ?? "").Trim();
        if (doc.Length < 2 || doc[0] != '{' || doc[^1] != '}') return;

        bool online = IsOnline(type);
        if (online) p.SeasonUserOnline = doc; else p.SeasonUserOffline = doc;

        var m = System.Text.RegularExpressions.Regex.Match(doc, "\"divisionId\"\\s*:\\s*(\\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int div) && div >= TopDivision && div <= BottomDivision)
        {
            if (online) p.OnlineDivision = div; else p.OfflineDivision = div;
        }
    }

    internal static void ClearSave(FutProfile p, string type)
    {
        if (IsOnline(type)) p.SeasonUserOnline = ""; else p.SeasonUserOffline = "";
    }

    internal static int ParseResetDivision(string path)
    {
        const string marker = "/division/";
        int i = path.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
        if (i < 0) return -1;
        int start = i + marker.Length;
        int end = start;
        while (end < path.Length && char.IsDigit(path[end])) end++;
        return end > start && int.TryParse(path[start..end], out int d) ? d : -1;
    }
}
