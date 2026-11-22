using System.Collections.Concurrent;
using Dapper;
using Poe1.Infrastructure;
using Poe1.Poe;

namespace Poe1.Builds;

public sealed class SavedBuild
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Source { get; set; }
    public string? Character { get; set; }
    public string CreatedTs { get; set; } = "";
}

/// <summary>Imports PoB builds, stores them with their ignore flags, and produces comparison reports.</summary>
public sealed class BuildService
{
    private readonly Database _db;
    private readonly HttpClient _http;
    private readonly CharacterGearService _gear;
    private readonly PassiveTreeData _tree;
    private readonly bool _demo;
    private readonly ConcurrentDictionary<long, PobBuild> _parsed = new();

    public BuildService(Database db, HttpClient http, CharacterGearService gear, PassiveTreeData tree, DemoMode demo)
    {
        _db = db;
        _http = http;
        _gear = gear;
        _tree = tree;
        _demo = demo.Enabled;
    }

    public List<SavedBuild> List()
    {
        using var c = _db.Open();
        return c.Query<SavedBuild>("SELECT id, name, source, character, created_ts AS CreatedTs FROM builds ORDER BY id DESC").ToList();
    }

    /// <summary>Accepts a PoB code, or a pobb.in / pastebin / poe.ninja link to one.</summary>
    public async Task<SavedBuild> ImportAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("Chưa nhập mã hoặc link build.");
        var code = input.Trim();
        string? source = null;
        if (PobParser.RawUrlFor(code) is { } url)
        {
            source = code;
            try
            {
                using var res = await _http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) throw new FormatException($"Không tải được build từ {url.Host} (HTTP {(int)res.StatusCode}).");
                code = (await res.Content.ReadAsStringAsync(ct)).Trim();
            }
            catch (HttpRequestException e)
            {
                throw new FormatException($"Không tải được build từ {url.Host}: {e.Message}");
            }
        }

        var xml = code.TrimStart().StartsWith('<') ? code : PobParser.DecodeCode(code);
        var build = PobParser.Parse(xml);
        if (build.Specs.Count == 0 && build.ItemSets.All(s => s.Slots.Count == 0) && build.SkillSets.All(s => s.Groups.Count == 0))
            throw new FormatException("Build không có cây passive, đồ hay gem nào.");

        var name = DefaultName(build);
        using var c = _db.Open();
        var id = c.ExecuteScalar<long>(
            "INSERT INTO builds(name, source, xml, created_ts) VALUES (@name, @source, @xml, @ts); SELECT last_insert_rowid();",
            new { name, source, xml, ts = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") });
        _parsed[id] = build;
        return List().First(b => b.Id == id);
    }

    private static string DefaultName(PobBuild build)
    {
        var skills = build.SkillSets.ElementAtOrDefault(build.ActiveSkillSetIndex) ?? build.SkillSets.FirstOrDefault();
        var main = skills?.Groups.OrderByDescending(g => g.Gems.Count).FirstOrDefault()?.Gems.FirstOrDefault(g => !g.IsSupport)?.Name;
        var who = build.Ascendancy ?? build.ClassName;
        return main is null ? who : $"{main} {who}";
    }

    public void Update(long id, string? name, string? character)
    {
        using var c = _db.Open();
        if (name is not null) c.Execute("UPDATE builds SET name = @name WHERE id = @id", new { id, name = name.Trim() });
        if (character is not null) c.Execute("UPDATE builds SET character = @character WHERE id = @id", new { id, character = character.Trim() });
    }

    public void Delete(long id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM builds WHERE id = @id", new { id });
        _parsed.TryRemove(id, out _);
    }

    public void SetIgnore(long id, string key, string? reason)
    {
        using var c = _db.Open();
        if (string.IsNullOrEmpty(reason))
            c.Execute("DELETE FROM build_ignores WHERE build_id = @id AND key = @key", new { id, key });
        else
            c.Execute("INSERT INTO build_ignores(build_id, key, reason) VALUES (@id, @key, @reason) ON CONFLICT(build_id, key) DO UPDATE SET reason = excluded.reason",
                new { id, key, reason });
    }

    private Dictionary<string, string> Ignores(long id)
    {
        using var c = _db.Open();
        return c.Query<(string Key, string Reason)>("SELECT key, reason FROM build_ignores WHERE build_id = @id", new { id })
            .ToDictionary(x => x.Key, x => x.Reason);
    }

    private PobBuild Load(long id)
    {
        if (_parsed.TryGetValue(id, out var b)) return b;
        using var c = _db.Open();
        var xml = c.ExecuteScalar<string?>("SELECT xml FROM builds WHERE id = @id", new { id })
                  ?? throw new KeyNotFoundException("Không tìm thấy build.");
        return _parsed[id] = PobParser.Parse(xml);
    }

    public async Task<List<CharacterInfo>> CharactersAsync(CancellationToken ct) =>
        _demo ? new List<CharacterInfo> { new() { Name = "DemoWitch", League = "Standard", Class = "Witch", Level = 80 } } : await _gear.CharactersAsync(ct);

    public async Task<BuildReport> ReportAsync(long id, string? character, int? spec, int? itemSet, int? skillSet, bool refresh, CancellationToken ct)
    {
        var build = Load(id);
        var saved = List().FirstOrDefault(b => b.Id == id) ?? throw new KeyNotFoundException("Không tìm thấy build.");
        character = string.IsNullOrWhiteSpace(character) ? saved.Character : character.Trim();
        if (string.IsNullOrWhiteSpace(character))
        {
            if (!_demo) throw new PoeApiException("Chọn nhân vật để so sánh.");
            character = "DemoWitch";
        }
        if (character != saved.Character) Update(id, null, character);

        var gear = _demo ? DemoGear.From(build) : await _gear.GetAsync(character, refresh, ct);
        var tree = await _tree.GetAsync(ct);
        return BuildComparer.Compare(build, gear, tree, spec, itemSet, skillSet, Ignores(id));
    }
}

/// <summary>Whether the app runs with --demo (made-up data, no account).</summary>
public sealed record DemoMode(bool Enabled);
