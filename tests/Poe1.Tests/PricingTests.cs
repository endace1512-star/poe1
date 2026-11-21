using System.Text.Json;
using Poe1.Poe;
using Poe1.Pricing;

namespace Poe1.Tests;

public class PricingTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static PoeItem Item(string json) => JsonSerializer.Deserialize<PoeItem>(json)!;

    private static PriceBook Book(params NinjaLine[] extra)
    {
        var (lines, divine) = NinjaClient.ParseExchange("Currency", Fixture("exchange-currency.json"));
        return new PriceBook("Test", DateTimeOffset.UtcNow, divine ?? 0, lines.Concat(extra).ToList());
    }

    private static NinjaLine Stash(string type, string name, double chaos, int listings = 50) =>
        new() { Type = type, Name = name, ChaosValue = chaos, ListingCount = listings, Category = type };

    [Fact]
    public void Exchange_prices_are_in_chaos_and_divine_comes_from_rates()
    {
        var (lines, divine) = NinjaClient.ParseExchange("Currency", Fixture("exchange-currency.json"));
        Assert.Equal(1 / 0.00448, divine!.Value, 3);
        Assert.Equal(223.2, lines.Single(l => l.Name == "Divine Orb").ChaosValue, 3);
        Assert.Equal(1, lines.Single(l => l.Name == "Chaos Orb").ChaosValue);
        Assert.All(lines, l => Assert.True(l.Exchange));
    }

    [Fact]
    public void Exchange_prices_convert_when_primary_is_divine()
    {
        const string json = """
            {"core":{"items":[],"rates":{"chaos":200},"primary":"divine","secondary":"chaos"},
             "lines":[{"id":"mirror","primaryValue":100}],
             "items":[{"id":"mirror","name":"Mirror of Kalandra","image":"/gen/image/x.png","category":"Currency"}]}
            """;
        var (lines, divine) = NinjaClient.ParseExchange("Currency", json);
        Assert.Equal(200, divine);
        Assert.Equal(20000, lines.Single(l => l.Name == "Mirror of Kalandra").ChaosValue);
        Assert.Equal("https://web.poecdn.com/gen/image/x.png", lines.Single(l => l.Name == "Mirror of Kalandra").Icon);
    }

    [Fact]
    public void Currency_stack_is_priced_by_type_line()
    {
        var pricer = new ItemPricer(Book());
        var p = pricer.Price(Item("""{"typeLine":"Divine Orb","frameType":5,"stackSize":13,"icon":"x"}"""));
        Assert.Equal("ex:Divine Orb", p.Key);
        Assert.Equal(13, p.Quantity);
        Assert.Equal(223.2 * 13, p.TotalChaos, 3);
        Assert.Equal("currency", p.Category);
    }

    [Fact]
    public void Unique_uses_link_count_and_ignores_low_confidence()
    {
        var book = Book(
            new NinjaLine { Type = "UniqueArmour", Name = "The Covenant", BaseType = "Spidersilk Robe", ChaosValue = 100, ListingCount = 30 },
            new NinjaLine { Type = "UniqueArmour", Name = "The Covenant", BaseType = "Spidersilk Robe", Links = 6, ChaosValue = 900, ListingCount = 30 },
            new NinjaLine { Type = "UniqueArmour", Name = "The Covenant", BaseType = "Spidersilk Robe", Links = 5, ChaosValue = 5000, ListingCount = 1 });
        var pricer = new ItemPricer(book, minListingCount: 3);

        const string sockets6 = """[{"group":0},{"group":0},{"group":0},{"group":0},{"group":0},{"group":0}]""";
        const string sockets5 = """[{"group":0},{"group":0},{"group":0},{"group":0},{"group":0},{"group":1}]""";
        var six = pricer.Price(Item($$"""{"name":"<<set:MS>><<set:M>><<set:S>>The Covenant","typeLine":"Spidersilk Robe","baseType":"Spidersilk Robe","frameType":3,"sockets":{{sockets6}}}"""));
        var five = pricer.Price(Item($$"""{"name":"The Covenant","typeLine":"Spidersilk Robe","frameType":3,"sockets":{{sockets5}}}"""));
        var none = pricer.Price(Item("""{"name":"The Covenant","typeLine":"Spidersilk Robe","frameType":3}"""));

        Assert.Equal(900, six.UnitChaos);
        Assert.Equal("The Covenant (6L)", six.Name);
        Assert.Equal(100, five.UnitChaos); // the 5L line has 1 listing: fall back to unlinked
        Assert.Equal(100, none.UnitChaos);
    }

    [Fact]
    public void Gem_uses_best_line_the_gem_meets()
    {
        NinjaLine Gem(int level, int? q, bool c, double chaos) => new()
            { Type = "SkillGem", Name = "Fireball", GemLevel = level, GemQuality = q, Corrupted = c, ChaosValue = chaos, ListingCount = 20 };
        var pricer = new ItemPricer(Book(Gem(1, null, false, 1), Gem(20, null, false, 4), Gem(20, 20, false, 20), Gem(21, 20, true, 9)));

        PricedItem Price(int level, int quality, bool corrupted) => pricer.Price(Item($$"""
            {"typeLine":"Fireball","frameType":4,"corrupted":{{(corrupted ? "true" : "false")}},
             "properties":[{"name":"Level","values":[["{{level}}{{(level >= 20 ? " (Max)" : "")}}",0]]},{"name":"Quality","values":[["+{{quality}}%",1]]}]}
            """));

        Assert.Equal(20, Price(20, 20, false).UnitChaos);
        Assert.Equal(4, Price(20, 10, false).UnitChaos);
        Assert.Equal(1, Price(12, 0, false).UnitChaos);
        Assert.Equal(9, Price(21, 20, true).UnitChaos);
        Assert.Equal(0, Price(20, 0, true).UnitChaos); // no corrupted 20/0 line
        Assert.Equal("Fireball (21/20c)", Price(21, 20, true).Name);
    }

    [Fact]
    public void Base_type_matches_ilvl_and_influence_order()
    {
        var pricer = new ItemPricer(Book(
            new NinjaLine { Type = "BaseType", Name = "Stygian Vise", LevelRequired = 86, Variant = "Shaper/Hunter", ChaosValue = 300, ListingCount = 10 },
            new NinjaLine { Type = "BaseType", Name = "Stygian Vise", LevelRequired = 85, ChaosValue = 5, ListingCount = 10 }));

        var influenced = pricer.Price(Item("""{"typeLine":"Stygian Vise","baseType":"Stygian Vise","frameType":2,"ilvl":87,"influences":{"hunter":true,"shaper":true}}"""));
        var plain = pricer.Price(Item("""{"typeLine":"Stygian Vise","frameType":0,"ilvl":85}"""));
        var low = pricer.Price(Item("""{"typeLine":"Stygian Vise","frameType":0,"ilvl":70}"""));

        Assert.Equal(300, influenced.UnitChaos);
        Assert.Equal(5, plain.UnitChaos);
        Assert.Equal("Stygian Vise (ilvl85)", plain.Name);
        Assert.Equal(0, low.UnitChaos);
    }

    [Fact]
    public void Cluster_jewel_matches_enchant_passives_and_ilvl()
    {
        var pricer = new ItemPricer(Book(
            new NinjaLine { Type = "ClusterJewel", Name = "Minions deal 10% increased Damage", BaseType = "Large Cluster Jewel", Variant = "8 passives", LevelRequired = 84, ChaosValue = 50, ListingCount = 10 },
            new NinjaLine { Type = "ClusterJewel", Name = "Minions deal 10% increased Damage", BaseType = "Large Cluster Jewel", Variant = "8 passives", LevelRequired = 1, ChaosValue = 10, ListingCount = 10 }));
        var p = pricer.Price(Item("""
            {"typeLine":"Large Cluster Jewel","frameType":0,"ilvl":84,
             "enchantMods":["Adds 8 Passive Skills","2 Added Passive Skills are Jewel Sockets","Added Small Passive Skills grant: Minions deal 10% increased Damage"]}
            """));
        Assert.Equal(50, p.UnitChaos);
        Assert.Equal("cluster", p.Category);
    }

    [Fact]
    public void Blighted_map_uses_tier_line()
    {
        var pricer = new ItemPricer(Book(Stash("BlightedMap", "Blighted Map (Tier 16)", 3)));
        var p = pricer.Price(Item("""{"typeLine":"Blighted Strand Map","baseType":"Strand Map","frameType":0,"properties":[{"name":"Map Tier","values":[["16",0]]}]}"""));
        Assert.Equal(3, p.UnitChaos);
        Assert.Equal("map", p.Category);
    }

    [Fact]
    public void Unknown_item_is_kept_with_zero_value()
    {
        var p = new ItemPricer(Book()).Price(Item("""{"name":"Doom Grip","typeLine":"Titan Gauntlets","frameType":2,"ilvl":60}"""));
        Assert.Equal(0, p.UnitChaos);
        Assert.Equal("Doom Grip Titan Gauntlets", p.Name);
    }
}
