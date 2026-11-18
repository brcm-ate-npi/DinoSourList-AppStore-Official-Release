using System.Text.RegularExpressions;
using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Assistant;

public sealed record Recommendation(ToolEntry Tool, double Score);
public sealed partial class ToolAssistant
{
    private readonly List<(ToolEntry Tool, Dictionary<string, double> Weights)> documents = [];
    private readonly Dictionary<string, double> idf = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Stop = new("a an the and or is are to for from with in on of i me my can could how what which tool please help need want use using this that it be by as do does have".Split(' '));
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Tokens();
    public static string[] Tokenize(string text) => Tokens().Matches(text.ToLowerInvariant()).Select(m => m.Value)
        .Where(t => t.Length > 1 && !Stop.Contains(t)).Select(t => t.Length > 5 && t.EndsWith("ing", StringComparison.Ordinal) ? t[..^3] : t.Length > 4 && t.EndsWith('s') ? t[..^1] : t).ToArray();
    public void Index(IEnumerable<(ToolEntry Tool, string Readme)> tools)
    {
        documents.Clear(); idf.Clear();
        var input = tools.Select(t => (t.Tool, Tokens: Tokenize(string.Join(' ', Enumerable.Repeat(t.Tool.Name, 3)) + " " + t.Tool.Category + " " + t.Readme))).ToArray();
        foreach (var entry in input)
            foreach (var word in entry.Tokens.Distinct()) idf[word] = idf.GetValueOrDefault(word) + 1;
        foreach (var word in idf.Keys) idf[word] = Math.Log((1.0 + input.Length) / (1.0 + idf[word])) + 1;
        foreach (var entry in input) documents.Add((entry.Tool, Vector(entry.Tokens)));
    }
    private Dictionary<string, double> Vector(IEnumerable<string> tokens)
    {
        var counts = tokens.Where(idf.ContainsKey).GroupBy(t => t).ToDictionary(g => g.Key, g => (1 + Math.Log(g.Count())) * idf[g.Key]);
        var norm = Math.Sqrt(counts.Values.Sum(v => v * v));
        return norm == 0 ? [] : counts.ToDictionary(k => k.Key, k => k.Value / norm);
    }
    public List<Recommendation> Recommend(string query, int count = 5)
    {
        var vector = Vector(Tokenize(query));
        return documents.Select(d => new Recommendation(d.Tool, vector.Sum(v => v.Value * d.Weights.GetValueOrDefault(v.Key))))
            .Where(r => r.Score > 0).OrderByDescending(r => r.Score).Take(Math.Clamp(count, 1, 20)).ToList();
    }
}
