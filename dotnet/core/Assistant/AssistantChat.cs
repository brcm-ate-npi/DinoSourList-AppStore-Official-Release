using System.Text.RegularExpressions;

namespace DinoSaurList.Core.Assistant;

public sealed record AssistantReply(string Text, List<Recommendation> Tools);

// Legacy deterministic intent/template behavior; every description is taken from an indexed README.
public sealed partial class AssistantChat(ToolAssistant assistant)
{
    private List<Recommendation> previous = [];
    private static readonly HashSet<string> SmallTalk = Words("hi hello hey howdy hiya greetings morning afternoon evening night thanks thank thx cheers ok okay cool great nice good fine sure how are you doing well wassup sup welcome bye cya later");
    private static readonly HashSet<string> Elaborate = Words("explain elaborate describe detail details overview feature features more about how");
    [GeneratedRegex("[a-z]+")]
    private static partial Regex WordPattern();
    private static HashSet<string> Words(string text) => WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
    public void Reset() => previous = [];
    public AssistantReply Reply(string query)
    {
        var words = Words(query); var meaningful = ToolAssistant.Tokenize(query).Length >= 2;
        var named = assistant.IndexedTools.Any(t => query.Contains(ToolAssistant.Metadata(assistant.ReadmeFor(t.Id), "TOOL", t.Name), StringComparison.OrdinalIgnoreCase));
        var elaborate = words.Overlaps(Elaborate);
        if (elaborate && !meaningful && !named && previous.Count > 0) return new(DescribeAll(previous), previous);
        if (!meaningful && (words.Count <= 6 && words.Overlaps(SmallTalk) || words.IsSubsetOf(SmallTalk)))
        {
            var response = words.Overlaps(Words("bye cya later")) ? "Goodbye! Have a wonderful day!"
                : words.Overlaps(Words("thanks thank thx cheers")) ? "You're welcome! Let me know if you need help finding a tool."
                : words.Overlaps(Words("hi hello hey howdy hiya greetings morning afternoon evening night")) ? "Hello! Welcome to WSD Dinosaur List.\nI'm here to help you find the right software tool.\nDescribe what you need and I'll find the best match!"
                : "I'm here to help! Describe the task or problem you'd like to solve\nand I'll find the best tool for you.";
            return new(response, []);
        }
        var browse = words.Overlaps(Words("list show display all available what which any related have got")) && words.Overlaps(Words("tool tools")) || words.Overlaps(Words("everything anything catalog catalogue"));
        if (browse)
        {
            var matches = assistant.Rank(query, 300, 0);
            var filtered = assistant.Rank(query, 300, .20);
            return new(Listing(filtered.Count > 0 ? "Here are the matching tools from our Dinosaur List:" : "Here are all the tools in our Dinosaur List:", filtered.Count > 0 ? filtered : matches, true) + "\n\nFeel free to ask me about any of them!", filtered.Count > 0 ? filtered : matches);
        }
        if (!meaningful && !named) return new("I couldn't detect a specific keyword in your message.\nPlease describe the task or problem you need help with — for example: 'band validation', 'data mapping', or 'GRR analysis'.", []);
        var results = assistant.Recommend(query);
        if (results.Count == 0) return new("I couldn't find a tool in the Dinosaur List that matches your request.\nPlease try describing your need more specifically.\nYou can also type 'list all tools' to browse what's available.", []);
        previous = results;
        if (results.Count > 1) return new(Listing($"I found {results.Count} matching tools:\n", results) + "\n\nType a tool name for details, or type 'describe' to see full details for all results.", results);
        var best = results[0];
        return new(elaborate ? Describe(best) : Header(best) + "\n" + ToolAssistant.Description(assistant.ReadmeFor(best.Tool.Id)) + "\n\nType 'describe' for more details.", results);
    }
    private string Header(Recommendation result)
    {
        var readme = assistant.ReadmeFor(result.Tool.Id);
        return $"{ToolAssistant.Metadata(readme, "TOOL", result.Tool.Name)}, developed by {ToolAssistant.Metadata(readme, "AUTHOR", result.Tool.Author)}.";
    }
    private string Listing(string heading, List<Recommendation> results, bool spaced = false)
    {
        var lines = new List<string> { heading };
        foreach (var (result, index) in results.Select((r, i) => (r, i)))
        {
            var readme = assistant.ReadmeFor(result.Tool.Id);
            lines.Add((spaced ? "\n" : "") + $"{index + 1}. {ToolAssistant.Metadata(readme, "TOOL", result.Tool.Name)}  —  by {ToolAssistant.Metadata(readme, "AUTHOR", result.Tool.Author)}");
            lines.Add("   " + ToolAssistant.Description(readme));
        }
        return string.Join('\n', lines);
    }
    private static string Plain(string text) => Regex.Replace(text, @"\*+(.+?)\*+", "$1").Trim();
    private string DescribeAll(List<Recommendation> results)
    {
        if (results.Count == 1) return Describe(results[0]);
        var output = new List<string>();
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i]; output.Add($"Tool {i + 1} of {results.Count}: {ToolAssistant.Metadata(assistant.ReadmeFor(result.Tool.Id), "TOOL", result.Tool.Name)}");
            output.Add(new string('─', 40)); output.Add(Describe(result)); if (i + 1 < results.Count) output.Add("");
        }
        return string.Join('\n', output);
    }
    private string Describe(Recommendation result)
    {
        var overview = new List<string>(); var features = new List<string>(); var uses = new List<string>();
        string? section = null; var formats = "";
        foreach (var raw in assistant.ReadmeFor(result.Tool.Id).Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0) continue;
            if (section is null && line.Contains(':') && !line.StartsWith('#') && !line.StartsWith('-'))
            {
                var parts = line.Split(':', 2); if (parts[0].Trim().Equals("formats", StringComparison.OrdinalIgnoreCase) && parts[1].Trim() is { Length: > 0 } value && !value.Equals("N/A", StringComparison.OrdinalIgnoreCase)) formats = value; continue;
            }
            var lower = line.ToLowerInvariant();
            if (lower is "## overview" or "## objective") { section = "overview"; continue; }
            if (lower is "## feature list" or "## features") { section = "features"; continue; }
            if (lower == "## use cases") { section = "usecases"; continue; }
            if (line.StartsWith("## ", StringComparison.Ordinal) || line == "---") { section = null; continue; }
            if (section is null && line.StartsWith("- **", StringComparison.Ordinal) && line.Contains(":**", StringComparison.Ordinal))
            {
                var parts = line.Split(":**", 2); if (parts[0].Contains("format", StringComparison.OrdinalIgnoreCase)) formats = parts[1].Trim(); continue;
            }
            if (section == "overview") overview.Add(Plain(line));
            if (section == "features" && line.StartsWith("- ", StringComparison.Ordinal)) features.Add(Plain(line[2..]));
            if (section == "usecases" && line.StartsWith("- ", StringComparison.Ordinal)) uses.Add(Plain(line[2..]));
        }
        var output = new List<string> { Header(result) + "\n" };
        if (overview.Count > 0) output.Add("Overview:\n" + overview[0] + "\n");
        if (formats.Length > 0) output.Add("Supported Formats: " + Plain(formats) + "\n");
        if (features.Count > 0) { output.Add("Key Features:"); output.AddRange(features.Take(8).Select(f => "  - " + f)); output.Add(""); }
        if (uses.Count > 0) { output.Add("Use Cases:"); output.AddRange(uses.Take(4).Select(f => "  - " + f)); }
        return string.Join('\n', output);
    }
}
