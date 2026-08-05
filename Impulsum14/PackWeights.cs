namespace Impulsum14;

internal static class PackWeights
{
    internal enum Group { GK, CB, FB, CM, WM, WF, ST }

    private static readonly HashSet<string> Fullbacks   = new() { "RB", "LB", "RWB", "LWB" };
    private static readonly HashSet<string> CentreMids  = new() { "CDM", "CM", "CAM" };
    private static readonly HashSet<string> WideMids     = new() { "RM", "LM" };
    private static readonly HashSet<string> WideForwards = new() { "RW", "LW", "RF", "LF" };
    private static readonly HashSet<string> Strikers     = new() { "ST", "CF" };

    internal static Group GroupOf(string position)
    {
        if (position == "GK") return Group.GK;
        if (position == "CB") return Group.CB;
        if (Fullbacks.Contains(position))   return Group.FB;
        if (WideMids.Contains(position))     return Group.WM;
        if (WideForwards.Contains(position)) return Group.WF;
        if (Strikers.Contains(position))     return Group.ST;
        return Group.CM;   // CDM/CM/CAM and any unknown position fall here
    }

    private const int BaseCommon = 140;
    private static int BaseRare(Group g) => g switch
    {
        Group.CB => 120,
        Group.FB => 100,
        _        => 105,
    };

    private enum Stat { Pace, Shooting, Passing, Dribbling, Defending, Physical,
                        Strength, BallControl, ShotPower, SkillMoves, FkAccuracy, Overall }

    private static int Value(RealPlayer p, Stat s) => s switch
    {
        Stat.Pace        => p.Pace,
        Stat.Shooting    => p.Shooting,
        Stat.Passing     => p.Passing,
        Stat.Dribbling   => p.Dribbling,
        Stat.Defending   => p.Defending,
        Stat.Physical    => p.Physical,
        Stat.Strength    => p.Strength,
        Stat.BallControl => p.BallControl,
        Stat.ShotPower   => p.ShotPower,
        Stat.SkillMoves  => p.SkillMoves,
        Stat.FkAccuracy  => p.FkAccuracy,
        _                => p.Rating,   // Overall
    };

    private readonly record struct Tune(Stat Stat, double Threshold, double Multiplier);

    private static readonly Dictionary<Group, Tune[]> Sheet = new()
    {

        [Group.GK] = new[]
        {
            new Tune(Stat.Overall, 76, 3.0),
        },
        [Group.CB] = new[]
        {
            new Tune(Stat.Defending, 72, 1.5), new Tune(Stat.Physical, 72, 1.0),
            new Tune(Stat.Strength, 74, 1.0),  new Tune(Stat.BallControl, 65, 0.4),
            new Tune(Stat.Overall, 76, 2.0),
        },
        [Group.FB] = new[]
        {
            new Tune(Stat.Pace, 78, 0.8),      new Tune(Stat.Defending, 70, 1.0),
            new Tune(Stat.Dribbling, 66, 0.6), new Tune(Stat.Physical, 66, 0.5),
            new Tune(Stat.Strength, 66, 0.4),  new Tune(Stat.BallControl, 66, 0.5),
            new Tune(Stat.Overall, 74, 2.0),
        },
        [Group.CM] = new[]
        {
            new Tune(Stat.Passing, 74, 1.0),    new Tune(Stat.BallControl, 74, 1.0),
            new Tune(Stat.Dribbling, 72, 0.8),  new Tune(Stat.ShotPower, 72, 0.5),
            new Tune(Stat.FkAccuracy, 65, 0.3), new Tune(Stat.Physical, 66, 0.4),
            new Tune(Stat.Overall, 76, 2.0),
        },
        [Group.WM] = new[]
        {
            new Tune(Stat.Pace, 78, 0.8),       new Tune(Stat.Dribbling, 74, 0.9),
            new Tune(Stat.Passing, 70, 0.7),    new Tune(Stat.SkillMoves, 3, 3.0),
            new Tune(Stat.BallControl, 74, 0.8), new Tune(Stat.Overall, 75, 2.0),
        },
        [Group.WF] = new[]
        {
            new Tune(Stat.Pace, 80, 0.9),       new Tune(Stat.Dribbling, 78, 1.0),
            new Tune(Stat.Shooting, 72, 0.8),   new Tune(Stat.SkillMoves, 4, 4.0),
            new Tune(Stat.BallControl, 78, 0.9), new Tune(Stat.FkAccuracy, 65, 0.3),
            new Tune(Stat.Overall, 78, 2.0),
        },
        [Group.ST] = new[]
        {
            new Tune(Stat.Shooting, 78, 1.2),   new Tune(Stat.ShotPower, 78, 0.9),
            new Tune(Stat.Physical, 72, 0.6),   new Tune(Stat.BallControl, 74, 0.8),
            new Tune(Stat.Dribbling, 74, 0.6),  new Tune(Stat.SkillMoves, 3, 1.5),
            new Tune(Stat.Strength, 74, 0.5),   new Tune(Stat.Overall, 78, 2.2),
        },
    };

    private const int MaxRating = 99;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _cache = new();

    internal static int Of(RealPlayer p)
    {
        if (_cache.TryGetValue(p.CardId, out int cached)) return cached;
        int w = Compute(p);
        _cache[p.CardId] = w;
        return w;
    }

    private static int Compute(RealPlayer p)
    {
        if (p.IsSpecial)
            return System.Math.Max(1, MaxRating - p.Rating);

        Group g = GroupOf(p.Position);
        double weight = p.Rare != 0 ? BaseRare(g) : BaseCommon;

        double penalty = 0;
        foreach (var t in Sheet[g])
            penalty += (t.Threshold - Value(p, t.Stat)) * t.Multiplier;

        weight += System.Math.Min(0.0, penalty);  
        if (p.Rating > 84) weight /= 4.0; 
        return System.Math.Max(1, (int)System.Math.Ceiling(weight));
    }
}
