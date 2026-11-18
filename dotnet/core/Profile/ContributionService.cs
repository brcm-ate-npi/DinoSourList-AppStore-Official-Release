using DinoSaurList.Core.Registry;

namespace DinoSaurList.Core.Profile;

public sealed record ContributionEvent(DateOnly Date, string Tool, string File, string Kind, string Author = "");
public sealed record ContributionDay(DateOnly Date, List<ContributionEvent> Events)
{
    public int Count => Events.Count;
}

public static class ContributionService
{
    public static List<ContributionDay> Calendar(IEnumerable<ToolEntry> tools, string author, int year, IEnumerable<ContributionEvent>? manual = null)
    {
        if (year is < 1970 or > 9998) throw new ArgumentOutOfRangeException(nameof(year));
        var events = new List<ContributionEvent>();
        foreach (var tool in tools.Where(t => t.Author.Equals(author, StringComparison.OrdinalIgnoreCase)))
            foreach (var file in tool.Files.Where(f => f.Type == "file"))
            {
                var created = file.CreatedAt is { } c ? DateOnly.FromDateTime(c.LocalDateTime) : (DateOnly?)null;
                var modified = file.ModifiedAt is { } m ? DateOnly.FromDateTime(m.LocalDateTime) : (DateOnly?)null;
                if (created is { } added && added.Year == year) events.Add(new(added, tool.Name, file.Name, "Added"));
                if (modified is { } edited && edited != created && edited.Year == year) events.Add(new(edited, tool.Name, file.Name, "Edited"));
            }
        if (manual is not null) events.AddRange(manual.Where(e => e.Date.Year == year && e.Author.Equals(author, StringComparison.OrdinalIgnoreCase)));
        var start = new DateOnly(year, 1, 1); var end = new DateOnly(year, 12, 31);
        var grouped = events.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.ToList());
        var days = new List<ContributionDay>();
        for (var day = start; day <= end; day = day.AddDays(1)) days.Add(new(day, grouped.GetValueOrDefault(day) ?? []));
        return days;
    }
    public static async Task<List<ContributionEvent>> LoadManualAsync(AppStorage storage, CancellationToken token = default)
    {
        var data = await storage.ReadAsync<System.Text.Json.JsonElement>("LegacyData/dev_activity.json", token);
        var result = new List<ContributionEvent>();
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object || !data.TryGetProperty("events", out var events)) return result;
        foreach (var entry in events.EnumerateArray())
        {
            if (!entry.TryGetProperty("date", out var date) || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)) continue;
            string Field(string name) => entry.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            result.Add(new(day, Field("app"), Field("version"), Field("kind"), Field("author")));
        }
        return result;
    }
}
