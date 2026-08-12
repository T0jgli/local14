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

    private static void AppendLadder(System.Text.StringBuilder sb, string type, int idBase)
    {
        bool online = type == "online";
        for (int div = 11; div >= 0; div--)
        {
            int step = 11 - div;                       // 0 at div 11 .. 11 at div 0
            int titleCoins = 1500 + step * 500;        // 1500 -> 7000
            int promotionCoins = 1100 + step * 300;    // 1100 -> 4400
            int maintenanceCoins = promotionCoins / 2; // stay-up reward
            int title = div == 1 ? 1 : 0;
            int id = idBase + div;

            if (sb.Length > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(id).Append(",\"seasonId\":").Append(id)
              .Append(",\"divisionId\":").Append(div).Append(",\"divisionOffline\":").Append(div)
              .Append(",\"divisionOnline\":").Append(div).Append(",\"leagueId\":").Append(div)
              .Append(",\"name\":\"").Append(online ? "Online " : "").Append("Division ").Append(div)
              .Append("\",\"title\":").Append(title).Append(",\"type\":\"").Append(type).Append("\",\"active\":true")
              .Append(",\"trophyResourceId\":").Append(8202000 + id)
              .Append(",\"numMatches\":").Append(Rounds).Append(",\"numRounds\":").Append(Rounds);

            sb.Append(",\"matches\":[");
            for (int r = 1; r <= Rounds; r++)
            {
                if (r > 1) sb.Append(',');
                sb.Append("{\"matchId\":").Append(r).Append(",\"round\":").Append(r).Append(",\"matchDifficulty\":5}");
            }
            sb.Append("]");

            sb.Append(",\"prizeSet\":[")
              .Append("{\"prizeLevel\":\"CHAMPIONSHIP\",\"thresholdPoint\":21,\"awardMappings\":[{\"timesWon\":1,\"awards\":[{\"type\":\"coin\",\"value\":").Append(titleCoins).Append(",\"count\":1}]}]},")
              .Append("{\"prizeLevel\":\"PROMOTION\",\"thresholdPoint\":15,\"awardMappings\":[{\"timesWon\":1,\"awards\":[{\"type\":\"coin\",\"value\":").Append(promotionCoins).Append(",\"count\":1}]}]},")
              .Append("{\"prizeLevel\":\"MAINTENANCE\",\"thresholdPoint\":6,\"awardMappings\":[{\"timesWon\":1,\"awards\":[{\"type\":\"coin\",\"value\":").Append(maintenanceCoins).Append(",\"count\":1}]}]}")
              .Append("]}");
        }
    }

    internal static string UserJson(FutProfile p) =>
        "{\"divisionId\":1,\"round\":1,\"seasonId\":-1}";

    internal static string TrophyJson(int entryId)
    {
        bool online = entryId >= 100;
        int div = online ? entryId - 100 : entryId;
        int design = 1100 + (11 - div);   // div 11 -> 1100 (bottom) .. div 0 -> 1111 (top)
        string label = (online ? "Online Division " : "Division ") + div;
        return "{\"tournamentId\":" + entryId + ",\"tournamentType\":0,\"assetName\":\"trophy_" + design +
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
