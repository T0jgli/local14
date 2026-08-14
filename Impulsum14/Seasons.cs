namespace Impulsum14;
internal static class Seasons
{
    private const int Rounds = 10;   // a season is 10 matches against a fixed fixture list

    internal static string ListJson()
    {
        var sb = new System.Text.StringBuilder();
        AppendLadder(sb, "offline", idBase: 0);
        AppendLadder(sb, "online", idBase: 100);
        string arr = sb.ToString();
        return "{\"seasons\":[" + arr + "],\"seasonData\":[" + arr + "],\"season\":[" + arr + "],\"totalResults\":24}";
    }

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

    private static void AppendLadder(System.Text.StringBuilder sb, string type, int idBase)
    {
        bool online = type == "online";
        string typeWire = online ? "ONLINE" : "OFFLINE";

        for (int div = 11; div >= 0; div--)
        {
            int realDiv = System.Math.Clamp(div, 1, 10);
            var d = DivRewards[realDiv];
            int id = idBase + div;
            int title = div == 1 ? 1 : 0;
            // Season Difficulty stars scale with the displayed division: bottom (10) = 1, top (1) = 5.
            int difficulty = System.Math.Clamp(6 - (realDiv + 1) / 2, 1, 5);

            if (sb.Length > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(id).Append(",\"seasonId\":").Append(id)
              .Append(",\"divisionId\":").Append(div).Append(",\"divisionOffline\":").Append(div)
              .Append(",\"divisionOnline\":").Append(div).Append(",\"leagueId\":").Append(div)
              .Append(",\"name\":\"").Append(online ? "Online " : "").Append("Division ").Append(realDiv)
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
        }
    }

    private static void AppendPrize(System.Text.StringBuilder sb, string level, int threshold, int coins, bool first)
    {
        if (!first) sb.Append(',');
        sb.Append("{\"prizeLevel\":\"").Append(level).Append("\",\"thresholdPoint\":").Append(threshold)
          .Append(",\"awardMappings\":[{\"timesWon\":1,\"awards\":[{\"type\":\"coin\",\"value\":").Append(coins)
          .Append(",\"count\":1}]}]}");
    }

    internal static string UserJson(FutProfile p) =>
        "{\"divisionId\":1,\"round\":1,\"seasonId\":-1}";

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

    internal static void CaptureSave(FutProfile p, string body)
    {
        string blob = BodyRx(body, "\"progressData\"\\s*:\\s*\"([^\"]*)\"");
        if (string.IsNullOrEmpty(blob))
            blob = BodyRx(body, "\"progressdata\"\\s*:\\s*\"([^\"]*)\"");
        if (!string.IsNullOrEmpty(blob)) p.SeasonSaveBlob = blob;
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

    private static string BodyRx(string body, string pattern)
    {
        if (string.IsNullOrEmpty(body)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(body, pattern);
        return m.Success ? m.Groups[1].Value : "";
    }
}
