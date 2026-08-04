namespace FIFAServer14;
internal static class Seasons
{

    internal static string ListJson()
    {
        var sb = new System.Text.StringBuilder();
        AppendSeasons(sb);
        string arr = sb.ToString();
        return "{\"seasons\":[" + arr + "],\"seasonData\":[" + arr + "],\"season\":[" + arr + "],\"totalResults\":12}";
    }

    private static void AppendSeasons(System.Text.StringBuilder sb)
    {
        for (int div = 11; div >= 0; div--)
        {
            int step = 11 - div;                       // 0 at div 11 .. 11 at div 0
            int titleCoins = 1500 + step * 500;        // 1500 -> 7000
            int promotionCoins = 1100 + step * 300;    // 1100 -> 4400
            int maintenanceCoins = promotionCoins / 2; // stay-up reward
            int title = div == 1 ? 1 : 0;              // matches the capture
            if (div < 11) sb.Append(',');
            sb.Append("{\"id\":").Append(div).Append(",\"seasonId\":").Append(div)
              .Append(",\"divisionId\":").Append(div).Append(",\"division\":").Append(div)
              .Append(",\"leagueId\":").Append(div).Append(",\"rank\":").Append(div)
              .Append(",\"tier\":").Append(div).Append(",\"divisionOnline\":").Append(div)
              .Append(",\"divisionOffline\":").Append(div).Append(",\"name\":\"Division ").Append(div)
              .Append("\",\"title\":").Append(title)
              .Append(",\"matchesTotal\":10,\"nummatchesplayed\":10,\"pointsForTitle\":21,")
              .Append("\"pointsForPromotion\":15,\"pointsForRelegation\":6,\"titleCoins\":").Append(titleCoins)
              .Append(",\"promotionCoins\":").Append(promotionCoins)
              .Append(",\"prizeSet\":[")
              .Append("{\"prizeLevel\":\"RELEGATION\",\"thresholdPoint\":0,\"awardSet\":{\"awards\":[{\"awardType\":1,\"value\":0,\"halid\":0}]}},")
              .Append("{\"prizeLevel\":\"MAINTENANCE\",\"thresholdPoint\":6,\"awardSet\":{\"awards\":[{\"awardType\":1,\"value\":").Append(maintenanceCoins).Append(",\"halid\":0}]}},")
              .Append("{\"prizeLevel\":\"PROMOTION\",\"thresholdPoint\":15,\"awardSet\":{\"awards\":[{\"awardType\":1,\"value\":").Append(promotionCoins).Append(",\"halid\":0}]}},")
              .Append("{\"prizeLevel\":\"CHAMPIONSHIP\",\"thresholdPoint\":21,\"awardSet\":{\"awards\":[{\"awardType\":1,\"value\":").Append(titleCoins).Append(",\"halid\":0}]}}")
              .Append("]")
              .Append(",\"difficulty\":5,\"active\":true,\"type\":\"offline\"}");
        }
    }

    internal static string UserJson(FutProfile p)
    {
        var s = p.Season;
        return "{\"offlineSeason\":{\"points\":" + s.Points + ",\"totalGames\":10,\"divisionId\":" + p.OfflineDivision +
               ",\"gamesPlayed\":" + s.GamesPlayed + ",\"progressDataVersion\":1,\"progressdata\":\"\",\"seasonId\":" + s.SeasonId +
               ",\"seasonGamesWon\":" + s.GamesWon + ",\"seasonGamesLost\":" + s.GamesLost + ",\"seasonGamesDraw\":" + s.GamesDraw +
               ",\"seasonTitlesWon\":" + s.TitlesWon + ",\"seasonPromotions\":" + s.Promotions + ",\"seasonRelegations\":" + s.Relegations +
               ",\"seasonCoins\":" + s.Coins + ",\"seasonCompleted\":" + (s.Completed ? "true" : "false") +
               "},\"divisionOffline\":" + p.OfflineDivision + "}";
    }

    internal static string ResetJson(int div) => "{\"offlineDivision\":" + div + "}";

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
