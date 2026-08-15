using System.Text;

namespace Impulsum14;

internal static class Market
{
    private static readonly RealPlayer[] Cards;  
    private static readonly int[] Counts;   
    private static readonly long[] Prefix;  
    internal static long Total { get; }
    private static readonly int HalfBits;       

    private const int TargetTotal = 2_000_000;    
    internal const long TradeIdBase = 2_000_000_000L; 
    private const long ItemIdBase = 3_000_000_000L;  

    static Market()
    {
        var pool = SpecialCards.All.Concat(RealPlayers.All).ToArray();
        Cards = pool.OrderBy(c => Hash((uint)c.CardId, 0x9E3779B1u)).ToArray();

        var raw = new int[Cards.Length];
        long rawSum = 0;
        for (int i = 0; i < Cards.Length; i++) { raw[i] = BaseCount(Cards[i]); rawSum += raw[i]; }
        double calib = rawSum > 0 ? (double)TargetTotal / rawSum : 1.0;

        Counts = new int[Cards.Length];
        Prefix = new long[Cards.Length + 1];
        long acc = 0;
        for (int i = 0; i < Cards.Length; i++)
        {
            int n = Math.Max(1, (int)Math.Round(raw[i] * calib));
            Counts[i] = n;
            acc += n;
            Prefix[i + 1] = acc;
        }
        Total = acc;

        HalfBits = HalfBitsFor(Total);

        Console.WriteLine($"[Market] simulated {Total:N0} listings across {Cards.Length:N0} cards");
    }

    private static int BaseCount(RealPlayer c)
    {
        int r = c.Rating;
        int b = r <= 64 ? 240 : r <= 74 ? 300 : r <= 79 ? 200 : r <= 82 ? 120
              : r <= 84 ? 70 : r <= 86 ? 35 : r <= 88 ? 16 : r <= 90 ? 7 : r <= 93 ? 3 : 2;
        if (c.IsSpecial) return Math.Max(2, (int)(b * 0.30));
        if (c.Rare != 0 && r >= 75) return (int)(b * 1.15);
        return b;
    }

    private static long BasePrice(RealPlayer c)
    {
        long p = RatingPrice(c.Rating);
        if (c.IsSpecial) return (long)(p * SpecialMult(c.Rare));
        if (c.Rare != 0 && c.Rating >= 75) return (long)(p * 1.25);
        return p;
    }

    private static long RatingPrice(int r) => r switch
    {
        <= 63 => 150,   64 => 170,   65 => 200,   66 => 220,   67 => 250,   68 => 280,
        69 => 320,      70 => 360,   71 => 420,   72 => 500,   73 => 600,   74 => 750,
        75 => 950,      76 => 1200,  77 => 1600,  78 => 2100,  79 => 2800,  80 => 3800,
        81 => 5200,     82 => 7000,  83 => 9500,  84 => 13000, 85 => 19000, 86 => 28000,
        87 => 42000,    88 => 65000, 89 => 100000, 90 => 160000, 91 => 260000, 92 => 430000,
        93 => 700000,   _ => 1100000,
    };

    private static double SpecialMult(int rareflag) => rareflag switch
    {
        5 => 8.0,    // TOTY
        11 => 5.0,   // TOTS
        3 => 2.0,    // in-form / TOTW
        _ => 2.5,
    };

    private static (int startingBid, int buyNow) Price(RealPlayer c, long g)
    {
        long baseP = BasePrice(c);
        var rng = new Rng(Hash((uint)c.CardId, (uint)g) ^ 0x51ED2C0Bu);
        long buy = Snap((long)(baseP * (0.80 + rng.NextDouble() * 0.55)));   // 0.80 .. 1.35
        long start = Snap((long)(buy * (0.55 + rng.NextDouble() * 0.30)));   // 0.55 .. 0.85
        if (start < 150) start = 150;
        if (start >= buy) start = Math.Max(150, buy - Step(buy));
        return ((int)start, (int)buy);
    }

    private static (int remaining, int cycle) Timer(long g, long now)
    {
        uint seed = Hash((uint)g, 0xB5297A4Du);
        int cycle = (seed % 4) switch { 0 => 1800, 1 => 3600, 2 => 7200, _ => 10800 };
        long off = seed % (uint)cycle;
        return ((int)(cycle - ((now + off) % cycle)), cycle);
    }

    private static long Step(long p) => p < 1000 ? 50 : p < 10000 ? 100 : p < 50000 ? 250 : p < 100000 ? 500 : 1000;
    private static long Snap(long p) => Math.Max(150, (p + Step(p) / 2) / Step(p) * Step(p));

    internal static string PageJson(int start, int num, long now, Func<RealPlayer, bool> match = null,
        int minBuyNow = 0, int maxBuyNow = 0, int minCurrent = 0, int maxCurrent = 0)
    {
        if (start < 0) start = 0;
        num = Math.Clamp(num, 1, 60);
        var rnd = new Random();
        var sb = new StringBuilder("[");
        int written = 0;

        static bool ByPrice(RealPlayer c, long g,
            int mnB, int mxB, int mnC, int mxC)
        {
            if (mnB <= 0 && mxB <= 0 && mnC <= 0 && mxC <= 0) return true;
            var (startBid, buyNow) = Price(c, g);
            if (mnB > 0 && buyNow < mnB) return false;
            if (mxB > 0 && buyNow > mxB) return false;
            if (mnC > 0 && startBid < mnC) return false;
            if (mxC > 0 && startBid > mxC) return false;
            return true;
        }

        if (match == null)
        {
            long scanCap = Math.Min(Total, start + (long)num * 16);
            for (long p = start; written < num && p < scanCap; p++)
            {
                long g = Permute(p);
                int i = Locate(g);
                if (!ByPrice(Cards[i], g, minBuyNow, maxBuyNow, minCurrent, maxCurrent)) continue;
                if (written > 0) sb.Append(',');
                sb.Append(Entry(Cards[i], g, now, rnd));
                written++;
            }
        }
        else
        {
            var mc = new List<int>();
            for (int i = 0; i < Cards.Length; i++) if (match(Cards[i])) mc.Add(i);
            if (mc.Count > 0)
            {
                var vPrefix = new long[mc.Count + 1];
                long acc = 0;
                for (int k = 0; k < mc.Count; k++) { acc += Counts[mc[k]]; vPrefix[k + 1] = acc; }
                long vTotal = acc;
                int vHalf = HalfBitsFor(vTotal);
                long scanCap = Math.Min(vTotal, start + (long)num * 16);
                for (long p = start; written < num && p < scanCap; p++)
                {
                    long fg = PermuteView(p, vTotal, vHalf);
                    int k = LocateIn(vPrefix, fg);
                    int ci = mc[k];
                    long g = Prefix[ci] + (fg - vPrefix[k]);   // real global index -> stable tradeId
                    if (!ByPrice(Cards[ci], g, minBuyNow, maxBuyNow, minCurrent, maxCurrent)) continue;
                    if (written > 0) sb.Append(',');
                    sb.Append(Entry(Cards[ci], g, now, rnd));
                    written++;
                }
            }
        }
        sb.Append(']');
        return sb.ToString();
    }

    internal static string EntryByTradeId(long tradeId, long now)
    {
        long g = tradeId - TradeIdBase;
        if (g < 0 || g >= Total) return null;
        return Entry(Cards[Locate(g)], g, now, new Random());
    }

    private static readonly string[] SellerNames =
        { "FUT", "LegacyFC", "UltimateXI", "TradeKing", "OldSchoolUT", "MarketFC", "FootyClub", "RareGoldFC" };

    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<long, long> MyBids = new();

    private static string Entry(RealPlayer card, long g, long now, Random rnd)
    {
        long itemId = ItemIdBase + g;
        long tradeId = TradeIdBase + g;
        var (start, buy) = Price(card, g);
        var (remaining, _) = Timer(g, now);
        string seller = SellerNames[(int)(g % SellerNames.Length)];
        string item = WebServer.BuildRealPlayerItem(rnd, card, itemId, now, 5, "forSale");
        long startBid = start;
        long myBid = MyBids.TryGetValue(tradeId, out long mb) ? mb : 0;
        long currentBid = myBid > 0 ? myBid : startBid;
        return "{\"tradeId\":" + tradeId + ",\"itemData\":" + item +
               ",\"tradeState\":\"active\",\"buyNowPrice\":" + buy +
               ",\"currentBid\":" + currentBid + ",\"offers\":0,\"watched\":null,\"bidState\":\"none\"," +
               "\"startingBid\":" + start + ",\"confidenceValue\":100,\"expires\":" + remaining +
               ",\"sellerName\":\"" + seller + "\",\"sellerEstablished\":2013," +
               "\"sellerId\":0,\"tradeOwner\":false,\"tradeIdStr\":\"" + tradeId +
               "\",\"lastSalePrice\":0,\"coinsProcessed\":false}";
    }

    internal static bool ResolveTradeId(long tradeId, out RealPlayer card, out int startingBid, out int buyNow)
    {
        card = default; startingBid = 0; buyNow = 0;
        long g = tradeId - TradeIdBase;
        if (g < 0 || g >= Total) return false;
        card = Cards[Locate(g)];
        (startingBid, buyNow) = Price(card, g);
        return true;
    }

    private static int Locate(long g) => LocateIn(Prefix, g);

    private static int LocateIn(long[] prefix, long g)
    {
        int lo = 0, hi = prefix.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (prefix[mid] <= g) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private static int HalfBitsFor(long total)
    {
        int hb = 1;
        while ((1L << (2 * hb)) < total) hb++;
        return hb;
    }

    private static long Permute(long p) => PermuteView(p, Total, HalfBits);

    private static long PermuteView(long p, long total, int half)
    {
        long g = Feistel(p, half);
        while (g >= total) g = Feistel(g, half);
        return g;
    }

    private static long Feistel(long x, int half)
    {
        long mask = (1L << half) - 1;
        long l = (x >> half) & mask;
        long r = x & mask;
        for (int i = 0; i < 4; i++)
        {
            long f = Hash((uint)r, (uint)(i * 0x9E3779B1u)) & mask;
            (l, r) = (r, l ^ f);
        }
        return (l << half) | r;
    }

    private static uint Hash(uint a, uint b)
    {
        uint h = a * 2654435761u ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2));
        h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
        return h;
    }

    private struct Rng
    {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }
        private uint NextU() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public double NextDouble() => (NextU() & 0xFFFFFF) / (double)0x1000000;
    }
}
