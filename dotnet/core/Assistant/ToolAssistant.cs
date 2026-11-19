using System.Text.RegularExpressions;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Assistant;

public sealed record Recommendation(ToolEntry Tool, double Score);
public sealed partial class ToolAssistant
{
    private readonly EnglishNormalizer normalizer = new();
    private List<(ToolEntry Tool, string Readme)> source = [];
    private readonly List<(ToolEntry Tool, string Name, string Readme, Dictionary<string, double> Weights)> documents = [];
    private readonly Dictionary<string, double> idf = new(StringComparer.Ordinal);
    private bool dirty;
    public static string[] Tokenize(string text) => new EnglishNormalizer().Preprocess(text);
    public void Index(IEnumerable<(ToolEntry Tool, string Readme)> tools) { source = tools.Where(t => !string.IsNullOrWhiteSpace(t.Readme)).ToList(); dirty = true; }
    private void EnsureIndex()
    {
        if (!dirty) return;
        documents.Clear(); idf.Clear();
        var input = source.Select(t => (t.Tool, t.Readme, Tokens: normalizer.Preprocess(t.Readme))).ToArray();
        foreach (var entry in input)
            foreach (var word in entry.Tokens.Distinct()) idf[word] = idf.GetValueOrDefault(word) + 1;
        foreach (var word in idf.Keys) idf[word] = Math.Log((1.0 + input.Length) / (1.0 + idf[word])) + 1;
        foreach (var entry in input) documents.Add((entry.Tool, Metadata(entry.Readme, "TOOL", entry.Tool.Name), entry.Readme, Vector(entry.Tokens)));
        dirty = false;
    }
    public static string Metadata(string readme, string key, string fallback)
    {
        foreach (var raw in readme.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) return line[(key.Length + 1)..].Trim();
            if (key == "TOOL" && line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
            if (key == "AUTHOR" && line.Contains("**Author:**", StringComparison.Ordinal)) return line.Split("**Author:**", 2)[1].Trim(' ', '-');
        }
        return fallback;
    }
    public static string Description(string readme)
    {
        var purpose = Metadata(readme, "PURPOSE", ""); if (purpose.Length > 0) return purpose;
        foreach (var target in new[] { "## Objective", "## Overview" })
        {
            var inside = false;
            foreach (var raw in readme.Split('\n'))
            {
                var line = raw.Trim(); if (line == target) { inside = true; continue; }
                if (inside && line.Length > 0 && !line.StartsWith('#') && !line.StartsWith('-')) return line.Split('.')[0] + ".";
            }
        }
        return "A tool available in the WSD Dinosaur List.";
    }
    private Dictionary<string, double> Vector(IReadOnlyList<string> tokens)
    {
        var counts = tokens.Where(idf.ContainsKey).GroupBy(t => t).ToDictionary(g => g.Key, g => (double)(float)(g.Count() / (double)Math.Max(1, tokens.Count) * idf[g.Key]));
        var norm = Math.Sqrt(counts.Values.Sum(v => v * v));
        return norm == 0 ? [] : counts.ToDictionary(k => k.Key, k => k.Value / norm);
    }
    public List<Recommendation> Recommend(string query, int count = 5)
    {
        EnsureIndex();
        // Prefer the full, longer tool name when names share a prefix (BandMaster/ReverseBin).
        var named = documents.Where(d => d.Name.Length > 0 && query.Contains(d.Name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.Name.Length).FirstOrDefault();
        if (named.Tool is not null) return [new(named.Tool, 1)];
        return Rank(query, count, .20);
    }
    public List<Recommendation> Rank(string query, int count = 20, double threshold = .20)
    {
        EnsureIndex(); var vector = Vector(normalizer.Preprocess(query));
        return documents.Select(d => new Recommendation(d.Tool, vector.Sum(v => v.Value * d.Weights.GetValueOrDefault(v.Key))))
            .Where(r => r.Score >= threshold).OrderByDescending(r => r.Score).Take(Math.Clamp(count, 1, 300)).Select(r => r with { Score = Math.Round(r.Score, 3) }).ToList();
    }
    public IReadOnlyList<ToolEntry> IndexedTools { get { EnsureIndex(); return documents.Select(d => d.Tool).ToList(); } }
    public string ReadmeFor(string id) => source.FirstOrDefault(d => d.Tool.Id == id).Readme ?? "";
}
