using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DinoSaurList.Core.Assistant;

// Inference-only C# adaptation of NLTK PerceptronTagger and WordNetLemmatizer.
// Python training/download machinery is absent. Attribution/licenses accompany the public model.
public sealed partial class EnglishNormalizer
{
    private sealed record Model(List<string> Stopwords, string[] Classes, Dictionary<string, Dictionary<string, double>> Weights,
        Dictionary<string, string> TagDictionary, Dictionary<string, HashSet<string>> Lemmas,
        Dictionary<string, Dictionary<string, string[]>> Exceptions, Dictionary<string, string[][]> Substitutions);
    private static readonly Lazy<Model> Data = new(() =>
    {
        var assembly = typeof(EnglishNormalizer).Assembly;
        using var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("english-nltk.json.gz", StringComparison.Ordinal)))!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Model>(gzip, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("English model is unavailable.");
    });
    private static readonly Lazy<HashSet<string>> Stop = new(() => Data.Value.Stopwords.ToHashSet(StringComparer.Ordinal));
    [GeneratedRegex("[a-zA-Z][a-zA-Z0-9]*")]
    private static partial Regex Words();
    private static string Suffix(string word) => word.Length > 3 ? word[^3..] : word;
    public string[] Preprocess(string text)
    {
        var words = Words().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length is > 1 and <= 1024 && !Stop.Value.Contains(w)).ToArray();
        var context = new[] { "-START-", "-START2-" }.Concat(words).Concat(["-END-", "-END2-"]).ToArray();
        var previous = "-START-"; var previous2 = "-START2-"; var result = new string[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i]; var offset = i + 2;
            if (!Data.Value.TagDictionary.TryGetValue(word, out var tag))
            {
                string[] features = ["bias", "i suffix " + Suffix(word), "i pref1 " + word[0], "i-1 tag " + previous,
                    "i-2 tag " + previous2, "i tag+i-2 tag " + previous + " " + previous2, "i word " + context[offset],
                    "i-1 tag+i word " + previous + " " + context[offset], "i-1 word " + context[offset - 1], "i-1 suffix " + Suffix(context[offset - 1]),
                    "i-2 word " + context[offset - 2], "i+1 word " + context[offset + 1], "i+1 suffix " + Suffix(context[offset + 1]), "i+2 word " + context[offset + 2]];
                var scores = new Dictionary<string, double>();
                foreach (var feature in features)
                    if (Data.Value.Weights.TryGetValue(feature, out var weights))
                        foreach (var weight in weights) scores[weight.Key] = scores.GetValueOrDefault(weight.Key) + weight.Value;
                tag = Data.Value.Classes.OrderByDescending(t => scores.GetValueOrDefault(t)).ThenByDescending(t => t, StringComparer.Ordinal).First();
            }
            var pos = tag[0] switch { 'J' => "a", 'V' => "v", 'R' => "r", _ => "n" };
            result[i] = PorterStemmer.Stem(Lemma(word, pos)); previous2 = previous; previous = tag;
        }
        return result;
    }
    private static string Lemma(string word, string pos)
    {
        var candidates = new List<string> { word };
        if (Data.Value.Exceptions[pos].TryGetValue(word, out var exceptions)) candidates.AddRange(exceptions);
        else foreach (var rule in Data.Value.Substitutions[pos])
                if (word.EndsWith(rule[0], StringComparison.Ordinal)) candidates.Add(word[..^rule[0].Length] + rule[1]);
        return candidates.Where(Data.Value.Lemmas[pos].Contains).OrderBy(w => w.Length).FirstOrDefault() ?? word;
    }
}
