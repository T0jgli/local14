namespace Impulsum14;

internal static class ItemTypes
{
    internal const string Player   = "player";
    internal const string Manager  = "manager";
    internal const string Staff    = "staff";     
    internal const string Kit      = "kit";
    internal const string Badge    = "badge";
    internal const string Ball     = "ball";
    internal const string Stadium  = "stadium";
    internal const string Contract = "contract";
    internal const string Fitness  = "fitness";
    internal const string Training  = "training";
    internal const string Healing  = "healing";
    internal const string Position = "position"; 
    internal const string Wildcard = "wildcard";
}
internal readonly record struct LevelVariant(int Weight, int Gold, int Silver, int Bronze);

internal readonly record struct ClassVariant(int Weight, int Rare);

internal readonly record struct TypeVariant(int Weight, (string Type, int Count)[] Fixed,
                                            int Wildcard, string WildcardPool);
internal readonly record struct WildcardPool(string Name, (string Type, int Weight)[] Options);

internal readonly record struct Guarantee(string Name, double ChancePct, string ItemType,
                                          bool Rare, int? Tier, bool Special);

internal sealed class PackDefinition
{
    public required int Size { get; init; }
    public required LevelVariant[] Levels { get; init; }
    public required ClassVariant[] Classes { get; init; }
    public required TypeVariant[] Types { get; init; }
    public WildcardPool[] Wildcards { get; init; } = System.Array.Empty<WildcardPool>();
    public Guarantee[] Guarantees { get; init; } = System.Array.Empty<Guarantee>();
    public bool Tradeable { get; init; } = true;
    public int MinPlayerRating { get; init; } = 0;
    public bool SpecialEligible => Guarantees.Any(g => g.Special);
}

internal static class PackConfig
{
    internal const int Bronze = 0, Silver = 1, Gold = 2;

    // Tier <-> rating band. Bronze 1-64, Silver 65-74, Gold 75-99.
    internal static (int Lo, int Hi) Band(int tier) => tier switch
    {
        Gold   => (75, 99),
        Silver => (65, 74),
        _      => (1, 64),
    };

    internal static int TierOf(int rating) => rating >= 75 ? Gold : rating >= 65 ? Silver : Bronze;

    internal static string TierWord(int tier) => tier switch { Gold => "Gold", Silver => "Silver", _ => "Bronze" };

    private static readonly WildcardPool SpecialSlotGold = new("specialGold", new[]
    {
        (ItemTypes.Kit, 24), (ItemTypes.Badge, 22), (ItemTypes.Ball, 16),
        (ItemTypes.Manager, 10), (ItemTypes.Staff, 10), (ItemTypes.Stadium, 8), (ItemTypes.Position, 12),
    });

    private static readonly WildcardPool SpecialSlot = new("special", new[]
    {
        (ItemTypes.Kit, 24), (ItemTypes.Badge, 22), (ItemTypes.Ball, 16),
        (ItemTypes.Manager, 10), (ItemTypes.Staff, 10), (ItemTypes.Stadium, 8),
    });

    private static TypeVariant StandardTypeMix(string wildcardPool) => new(
        Weight: 1,
        Fixed: new[]
        {
            (ItemTypes.Player, 4), (ItemTypes.Contract, 4),
            (ItemTypes.Fitness, 1), (ItemTypes.Training, 1), (ItemTypes.Healing, 1),
        },
        Wildcard: 1,
        WildcardPool: wildcardPool);

    private static PackDefinition Standard(int tier, int rareCount, bool gold) => new()
    {
        Size = 12,
        Levels = new[]
        {
            new LevelVariant(1,
                Gold:   tier == Gold   ? 12 : 0,
                Silver: tier == Silver ? 12 : 0,
                Bronze: tier == Bronze ? 12 : 0),
        },

        Classes = rareCount >= 3
            ? new[] { new ClassVariant(85, 3), new ClassVariant(15, 4) }
            : new[] { new ClassVariant(88, 1), new ClassVariant(12, 2) },
        Types = new[] { StandardTypeMix(gold ? SpecialSlotGold.Name : SpecialSlot.Name) },
        Wildcards = new[] { gold ? SpecialSlotGold : SpecialSlot },

        Guarantees = new[]
        {
            new Guarantee("rarePlayer", 100, ItemTypes.Player, Rare: true, Tier: null, Special: false),
        },
    };

    private static PackDefinition PlayersPack(int size, int rareCount,
                                              LevelVariant[] levels = null) => new()
    {
        Size = size,
        Levels = levels ?? new[] { new LevelVariant(1, Gold: size, Silver: 0, Bronze: 0) },
        Classes = new[] { new ClassVariant(1, System.Math.Min(rareCount, size)) },
        Types = new[] { new TypeVariant(1, new[] { (ItemTypes.Player, size) }, 0, "") },
        Guarantees = new[]
        {
            new Guarantee("rarePlayer", 100, ItemTypes.Player, Rare: true, Tier: PackConfig.Gold, Special: false),
        },
    };

    internal static readonly Dictionary<int, PackDefinition> Packs = new()
    {
        [100] = Standard(Bronze, 1, gold: false),   // Bronze Pack
        [103] = Standard(Bronze, 3, gold: false),   // Premium Bronze Pack
        [200] = Standard(Silver, 1, gold: false),   // Silver Pack
        [203] = Standard(Silver, 3, gold: false),   // Premium Silver Pack
        [300] = Standard(Gold,   1, gold: true),    // Gold Pack
        [304] = Standard(Gold,   3, gold: true),    // Premium Gold Pack
        [405] = PlayersPack(12, 8, new[]            //30k pack
        {
            new LevelVariant(80, Gold: 12, Silver: 0, Bronze: 0),
            new LevelVariant(20, Gold: 11, Silver: 1, Bronze: 0),
        }),
        [406] = PlayersPack(24, 24),   // 50k pack
        [404] = new()                  // 100k pack
        {
            Size = 30,
            Levels = new[] { new LevelVariant(1, Gold: 30, Silver: 0, Bronze: 0) },
            Classes = new[] { new ClassVariant(1, 30) },
            Types = new[] { new TypeVariant(1, new[] { (ItemTypes.Player, 30) }, 0, "") },
            Guarantees = new[]
            {
                new Guarantee("rarePlayer", 100, ItemTypes.Player, Rare: true, Tier: Gold, Special: false),
            },
            MinPlayerRating = 75,  
        },
    };
}
