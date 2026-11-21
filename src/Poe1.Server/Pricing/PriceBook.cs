namespace Poe1.Pricing;

/// <summary>An immutable, indexed set of poe.ninja prices for one league.</summary>
public sealed class PriceBook
{
    public string League { get; }
    public DateTimeOffset FetchedAt { get; }
    public double DivineChaos { get; }
    public IReadOnlyList<NinjaLine> Lines { get; }

    private readonly Dictionary<string, NinjaLine> _exchange = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<NinjaLine>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<NinjaLine>> _baseTypes = new(StringComparer.OrdinalIgnoreCase);

    public PriceBook(string league, DateTimeOffset fetchedAt, double divineChaos, IReadOnlyList<NinjaLine> lines)
    {
        League = league;
        FetchedAt = fetchedAt;
        Lines = lines;
        foreach (var l in lines)
        {
            if (l.Exchange)
            {
                _exchange.TryAdd(l.Name, l);
                continue;
            }
            var index = l.Type == "BaseType" ? _baseTypes : _byName;
            if (!index.TryGetValue(l.Name, out var list)) index[l.Name] = list = new List<NinjaLine>();
            list.Add(l);
        }
        if (divineChaos <= 0 && _exchange.TryGetValue("Divine Orb", out var div)) divineChaos = div.ChaosValue;
        DivineChaos = divineChaos;
    }

    public static PriceBook Empty(string league) => new(league, DateTimeOffset.MinValue, 0, Array.Empty<NinjaLine>());

    public NinjaLine? Exchange(string name) => _exchange.GetValueOrDefault(name);

    public IReadOnlyList<NinjaLine> ByName(string name) =>
        _byName.TryGetValue(name, out var l) ? l : Array.Empty<NinjaLine>();

    public IReadOnlyList<NinjaLine> BaseType(string name) =>
        _baseTypes.TryGetValue(name, out var l) ? l : Array.Empty<NinjaLine>();

    /// <summary>Free-text lookup for the UI (case-insensitive substring), most valuable first.</summary>
    public IEnumerable<NinjaLine> Search(string query, int limit = 50) =>
        Lines.Where(l => l.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(l => l.ChaosValue)
             .Take(limit);
}
