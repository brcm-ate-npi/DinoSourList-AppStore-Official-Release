using System.Text.RegularExpressions;

namespace DinoSaurList.Core.Registry;

public sealed record FlowDocument(Dictionary<string, Dictionary<string, string>> Sections)
{
    public static FlowDocument Parse(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "";
        foreach (var raw in text.TrimStart('\ufeff').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim().ToLowerInvariant();
                sections.TryAdd(current, new(StringComparer.OrdinalIgnoreCase));
            }
            else if (current.Length > 0 && line.IndexOf('=') is var split && split > 0)
                sections[current][line[..split].Trim().ToLowerInvariant()] = line[(split + 1)..].Trim();
        }
        return new(sections);
    }

    public string Get(string section, string key, string fallback = "") =>
        Sections.FirstOrDefault(s => s.Key.Equals(section, StringComparison.OrdinalIgnoreCase)).Value?
            .FirstOrDefault(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? fallback;
    public bool Flag(string section, string key) => Get(section, key).Equals("true", StringComparison.OrdinalIgnoreCase);
    public string[] Platforms
    {
        get
        {
            var raw = Get("platform", "supported", Get("platform", "os", Get("platform", "platforms", "windows")));
            var values = Regex.Split(raw.ToLowerInvariant(), @"[\s,;|]+")
                .Select(x => x switch { "win" or "win-x64" or "win32" => "windows", "mac" or "osx" or "osx-arm64" or "osx-x64" => "macos", "all" => "any", _ => x })
                .Where(x => x is "windows" or "macos" or "linux" or "any").Distinct().ToArray();
            return values.Length == 0 ? ["windows"] : values;
        }
    }
}

public static partial class ToolVersion
{
    public static IComparer<string> Comparer { get; } = System.Collections.Generic.Comparer<string>.Create(Compare);
    public static int Compare(string? a, string? b)
    {
        var left = Parts(a); var right = Parts(b);
        for (var i = 0; i < 4; i++) { var c = left[i].CompareTo(right[i]); if (c != 0) return c; }
        return 0;
    }
    private static long[] Parts(string? value)
    {
        var result = new long[4]; var parts = (value ?? "").TrimStart('v', 'V').Split('.');
        for (var i = 0; i < Math.Min(4, parts.Length); i++) long.TryParse(parts[i], out result[i]);
        return result;
    }
    [GeneratedRegex(@"^v?(\d+(?:\.\d+){1,3})(?:[-_](win(?:dows)?(?:-x64)?|osx-(?:arm64|x64)|mac(?:os)?|linux(?:-x64)?|any))?\.zip$", RegexOptions.IgnoreCase)]
    public static partial Regex PackagePattern();
    [GeneratedRegex(@"^(.+?)[-_]V-([^_-]+?)[-_]A-(.+)$", RegexOptions.IgnoreCase)]
    public static partial Regex InstalledPattern();
}
