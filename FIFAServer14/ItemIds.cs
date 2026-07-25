namespace FIFAServer14;

internal static class ItemIds
{
    internal const long PlayerBase = 500_000_000L;

    internal static long For(RealPlayer p) => PlayerBase + p.CardId;

    internal static bool TryResolve(long itemId, out RealPlayer player)
    {
        player = default;
        long cardId = itemId - PlayerBase;
        if (cardId <= 0 || cardId > int.MaxValue) return false;
        return ByCardId.Value.TryGetValue((int)cardId, out player);
    }

    private static readonly Lazy<Dictionary<int, RealPlayer>> ByCardId = new(() =>
    {
        var map = new Dictionary<int, RealPlayer>();
        foreach (var p in RealPlayers.All) map[p.CardId] = p;
        foreach (var p in SpecialCards.All) map[p.CardId] = p;
        return map;
    });
}
