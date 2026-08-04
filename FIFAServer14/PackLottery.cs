namespace FIFAServer14;

internal static class PackLottery
{
    internal static T Draw<T>(IReadOnlyList<T> pool, System.Func<T, int> weight, System.Random rnd)
    {
        T best = default!;
        double bestKey = double.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < pool.Count; i++)
        {
            int w = weight(pool[i]);
            if (w <= 0) continue;    
            double u = rnd.NextDouble();
            if (u <= 0) u = double.Epsilon;   
            double key = -System.Math.Log(1.0 - u) / w;
            if (key < bestKey)
            {
                bestKey = key;
                best = pool[i];
                found = true;
            }
        }
        if (!found)             
            return pool[rnd.Next(pool.Count)];
        return best;
    }

    internal static T DrawExcluding<T>(IReadOnlyList<T> pool, System.Func<T, int> weight,
                                       System.Func<T, bool> excluded, System.Random rnd)
    {
        var eligible = new List<T>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
            if (!excluded(pool[i])) eligible.Add(pool[i]);
        if (eligible.Count == 0) return Draw(pool, weight, rnd);
        return Draw(eligible, weight, rnd);
    }
}
