using DinoSaurList.Core.Profile;
using DinoSaurList.Core.Registry;
using Xunit;

namespace DinoSaurList.Tests;

public sealed class ContributionTests
{
    [Fact]
    public void CalendarUsesRealFileDatesAndNeverInventsSampleContributions()
    {
        var created = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var tool = new ToolEntry("1", "Tool-A", "Tool", "A", "MISC", [], FlowDocument.Parse(""),
            [new("2", "v1.0.zip", "file", CreatedAt: created, ModifiedAt: created.AddDays(2)), new("3", "README.md", "file", CreatedAt: created, ModifiedAt: created)]);
        var days = ContributionService.Calendar([tool], "A", 2026);
        Assert.Equal(365, days.Count); Assert.Equal(3, days.Sum(d => d.Count));
        Assert.Equal(2, days.Single(d => d.Date == DateOnly.FromDateTime(created.LocalDateTime)).Count);
        Assert.All(ContributionService.Calendar([tool], "Other", 2026), d => Assert.Equal(0, d.Count));
        Assert.Equal(366, ContributionService.Calendar([], "A", 2024).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => ContributionService.Calendar([], "A", 10000));
    }
}
