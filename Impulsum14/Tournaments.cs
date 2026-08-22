namespace Impulsum14;

internal static class Tournaments
{
    private static readonly Dictionary<int, int[]> TeamPools = new()
    {
        [ 1] = new[] { 422, 1572, 357, 294, 922, 1914, 1744, 873, 697, 689, 1939, 110, 696, 12, 15005 },
        [ 2] = new[] { 1926, 162, 2023, 433, 94, 298, 165, 1910, 256, 1871, 570, 97, 3, 8, 1880 },
        [ 3] = new[] { 1887, 1902, 62, 1807, 1915, 417, 614, 191, 91, 665, 200, 459, 95, 14, 1884 },
        [ 4] = new[] { 57, 378, 605, 2007, 1888, 472, 190, 674, 1913, 10020, 226, 1882, 29, 1795, 71 },
        [ 5] = new[] { 896, 1906, 1903, 31, 673, 1881, 1808, 58, 1844, 379, 242, 1838, 1901, 4, 1876 },
        [ 6] = new[] { 203, 1861, 1799, 1793, 78, 171, 453, 1893, 1837, 1909, 10029, 1878, 232, 1908, 15029 },
        [ 7] = new[] { 229, 1961, 217, 744, 1598, 1952, 231, 246, 1892, 468, 244, 189, 1032, 192, 166 },
        [ 8] = new[] { 72, 479, 1917, 206, 1970, 1879, 169, 1039, 819, 1853, 1843, 25, 1891, 483, 54 },
        [ 9] = new[] { 2, 38, 1013, 1719, 109, 450, 485, 1792, 70, 106, 59, 1824, 1809, 28, 1877 },
        [10] = new[] { 19, 247, 1028, 15, 1806, 66, 569, 452, 23, 312, 32, 36, 383, 1842, 245 },
        [11] = new[] { 17, 39, 1819, 393, 65, 462, 517, 315, 480, 567, 1053, 219, 1860, 50, 9 },
        [12] = new[] { 1960, 568, 280, 74, 1629, 598, 237, 1043, 69, 175, 449, 448, 573, 1, 481 },
        [13] = new[] { 13, 7, 144, 1896, 34, 1048, 55, 1035, 1041, 461, 22, 18, 457, 44, 48 },
        [14] = new[] { 52, 234, 325, 236, 47, 46, 10, 5, 11, 21, 73, 240, 45, 243, 241 },
    };

    private static readonly int[] DefaultTeamIds =
        { 52, 234, 325, 236, 47, 46, 10, 5, 11, 21, 73, 240, 45, 243, 241 };

    internal static int[] GetTeamIds(int tournamentId) =>
        TeamPools.TryGetValue(tournamentId, out var pool) ? pool : DefaultTeamIds;

    internal static readonly (int Id, string Name, int Design, int Diff, int Coins, int Unlock)[] Defs =
    {
        ( 1, "Starter Cup",                1100, 1,  300,  0),  // Amateur,      first win 500 + Club Customisation Pack
        ( 2, "Midlands Invitational",      1104, 2,  500,  0),  // Semi-Pro,     first win 600
        ( 3, "Gold Challenge",             1108, 3,  700,  0),  // Professional, first win 1000
        ( 4, "Quad-League Classic",        1112, 2,  600,  1),  // Semi-Pro,     first win 700
        ( 5, "Managers Cup",               1116, 3,  700,  1),  // Professional, first win 700 + Silver Gift Pack
        ( 6, "Bronze International Shield",1120, 4, 1000,  2),  // World Class,  first win 1000 + Gold Gift Pack
        ( 7, "Trio Showcase",              1124, 2,  300,  2),  // Semi-Pro,     first win Silver Contracts Pack
        ( 8, "Unified Cup",                1128, 3, 1000,  2),  // Professional, first win 1250
        ( 9, "Pyramid Invitational",       1132, 4, 1000,  3),  // World Class,  first win 1000 + Mixed Contracts Pack
        (10, "Silver Links Cup",           1136, 3,  700,  4),  // Professional, first win 1000
        (11, "Federation Cup",             1140, 4,  200,  4),  // World Class,  first win 2000
        (12, "Champions Trophy",           1144, 5, 2500,  4),  // Legendary,    first win 2500
        (13, "Premier Clash",              1148, 3, 1200,  5),  // Professional, first win 1500
        (14, "Ultimate Cup",               1152, 3, 3000, 10),  // Professional, first win 3000 + Gold Pack
    };

    internal static string CatalogJson()
    {
        var sb = new System.Text.StringBuilder("{\"tournament\":[");
        for (int i = 0; i < Defs.Length; i++)
        {
            var (id, _, _, diff, coins, unlock) = Defs[i];
            int trophy = 8200000 + id;   // trophy doc lives at fut/items/pc/<trophy>.json
            if (i > 0) sb.Append(',');
            string lockState = unlock > TrophiesWon ? "LOCKED_TROPHIES" : "UNLOCKED";
            sb.Append("{\"id\":").Append(id).Append(",\"tournamentId\":").Append(id)
              .Append(",\"tournamentType\":0,\"type\":\"offline\",\"numTeams\":16,\"numRounds\":4,")
              .Append("\"numMatches\":4,\"matchlength\":6,\"starttime\":0,\"timeUntilStart\":0,")
              .Append("\"timeUntilEnd\":31536000,\"trophyResourceId\":").Append(trophy)
              .Append(",\"trophyUserCount\":").Append(TrophiesWon).Append(",\"triesMax\":0,\"treeType\":0,\"lock\":\"").Append(lockState).Append("\",")
              .Append("\"unlockreq\":").Append(unlock).Append(",\"nextReset\":0,\"visStart\":0,\"visEnd\":0,\"rounds\":[");
            for (int r = 1; r <= 4; r++)
            {
                if (r > 1) sb.Append(',');
                sb.Append("{\"id\":").Append(r).Append(",\"difficulty\":").Append(diff)
                  .Append(",\"rewardMultiplier\":1,\"coins\":").Append(r == 4 ? coins : 0).Append('}');
            }
            sb.Append("],\"awardSet\":{\"awards\":[{\"awardType\":1,\"value\":").Append(coins).Append(",\"halid\":0}]}}");
        }
        return sb.Append("]}").ToString();
    }

    internal static string TrophyJson(int tourneyId)
    {
        string name = "Cup"; int design = 1100;
        foreach (var d in Defs)
            if (d.Id == tourneyId) { name = d.Name; design = d.Design; break; }
        return "{\"tournamentId\":" + tourneyId + ",\"tournamentType\":0,\"assetName\":\"trophy_" + design +
               "_gold\",\"silName\":\"trophy_" + design + "_dark\",\"locString\":[{\"lang\":\"ENG_US\",\"label\":\"" +
               name + "\"}]}";
    }

    internal static int ActiveTournamentId = 1;

    internal static int TrophiesWon => FutProfileStore.Get().TrophiesWon;

    internal const int NumRounds = 4;                       // every fixed cup is a 16-team, 4-round bracket
    internal static int? CurrentMatchTournamentId = null;   // set on POST /match for a tournament match

    internal static int AwardCoins(int tournamentId)
    {
        foreach (var d in Defs) if (d.Id == tournamentId) return d.Coins;
        return 0;
    }

    internal static string TeamsJson(int tournamentId = 0)
    {
        int tid = tournamentId > 0 ? tournamentId : ActiveTournamentId;
        int[] ids = GetTeamIds(tid);
        string arr = "[" + string.Join(",", ids) + "]";
        return "{\"team\":" + arr + ",\"teams\":" + arr + ",\"teamIds\":" + arr + ",\"teamId\":" + arr +
               ",\"entries\":" + arr + ",\"list\":" + arr + ",\"data\":" + arr + ",\"results\":" + arr +
               ",\"totalResults\":" + ids.Length + "}";
    }

    internal static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        return sb.ToString();
    }

    internal static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                switch (n)
                {
                    case '"':  sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/'); break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u' when i + 4 < s.Length &&
                                   int.TryParse(s.Substring(i + 1, 4),
                                       System.Globalization.NumberStyles.HexNumber,
                                       System.Globalization.CultureInfo.InvariantCulture, out int uv):
                        sb.Append((char)uv);
                        i += 4;
                        break;
                    default:   sb.Append(n); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    internal static string CaptureString(string body, string field)
    {
        var m = System.Text.RegularExpressions.Regex.Match(body ?? "",
            "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? UnescapeJson(m.Groups[1].Value) : "";
    }

    internal static string UserTournamentJson(int id)
    {
        var s = FutProfileStore.Get().SavedTournaments.GetValueOrDefault(id);
        if (s is null)
            return "{\"tournamentId\":" + id + "}";
        return "{\"tournamentId\":" + id + ",\"round\":" + s.Round +
               ",\"dataVersion\":" + s.DataVersion +
               ",\"tournamentData\":\"" + EscapeJson(s.TournamentData) + "\"" +
               ",\"progressDataVersion\":" + s.ProgressDataVersion +
               ",\"progressData\":\"" + EscapeJson(s.ProgressData) + "\"}";
    }

    internal static string UserListJson()
    {
        var ids = FutProfileStore.Get().SavedTournaments.Keys.OrderBy(k => k);
        return "{\"tournamentId\":[" + string.Join(",", ids) + "]}";
    }

    internal static string SaveProgress(int id, int round, int dataVersion, string? tournamentData,
                                        int progressDataVersion, string? progressData)
    {
        tournamentData ??= "";
        progressData ??= "";
        FutProfileStore.Mutate(p =>
        {
            var s = p.SavedTournaments.TryGetValue(id, out var e) ? e : new SavedTournament();
            s.Round = round;
            s.DataVersion = dataVersion;
            s.TournamentData = tournamentData;
            s.ProgressDataVersion = progressDataVersion;
            s.ProgressData = progressData;
            s.Active = true;
            p.SavedTournaments[id] = s;
        });
        return "{\"tournamentId\":" + id + ",\"round\":" + round + ",\"dataVersion\":" + dataVersion +
               ",\"tournamentData\":\"" + EscapeJson(tournamentData) + "\",\"progressDataVersion\":" +
               progressDataVersion + ",\"progressData\":\"" + EscapeJson(progressData) + "\"}";
    }

    internal static (int Prize, bool WonFinal) SettleTournamentMatch(int id, string? endReason)
    {
        string r = (endReason ?? "").ToUpperInvariant();
        int prize = 0;
        bool wonFinal = false;
        FutProfileStore.Mutate(p =>
        {
            var s = p.SavedTournaments.TryGetValue(id, out var e) ? e : new SavedTournament();
            int cur = Math.Clamp(s.Round, 1, NumRounds);
            if (r == "WIN")
            {
                if (cur < NumRounds)
                {
                    // Do NOT advance s.Round here. The client is the sole bracket authority —
                    // it will PUT the updated round + new blobs on re-entry. If we bumped round
                    // here the GET on re-entry would return round=N+1 but tournamentData/progressData
                    // would still be the old round-N blobs, causing a round-vs-blob mismatch that
                    // crashes the game client.
                    s.Active = true;
                    p.SavedTournaments[id] = s;
                }
                else
                {
                    prize = AwardCoins(id);            // final-round win: award the cup prize once
                    wonFinal = true;
                    p.SavedTournaments.Remove(id);     // run complete -> replayable, no longer underway
                }
            }
            else if (r is "LOSS" or "DNF" or "QUIT")
            {
                p.SavedTournaments.Remove(id);         // run over -> cup can be entered again
            }
        });
        return (prize, wonFinal);
    }
}
