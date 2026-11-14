using System.Text;
using Extatic.Api.Features.Events;

namespace Extatic.Api.Tests.Features.Events;

public class IcsBuilderTests
{
    private static Event Sample() => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Title = "Launch; party, \\ now",
        Description = "Line one\r\nLine two\nBEGIN:VEVENT",
        Location = "Hall A, 1st floor",
        StartsAt = new DateTime(2026, 12, 1, 18, 30, 0, DateTimeKind.Utc),
        EndsAt = new DateTime(2026, 12, 1, 21, 0, 0, DateTimeKind.Utc),
        Status = EventStatus.Published,
        UpdatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Build_EscapesTextAndUsesUtc()
    {
        var ics = IcsBuilder.Build(Sample(), "My \"Site\"", new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc));
        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", ics);
        Assert.Contains("\r\nUID:11111111-2222-3333-4444-555555555555@extatic\r\n", ics);
        Assert.Contains("\r\nDTSTART:20261201T183000Z\r\n", ics);
        Assert.Contains("\r\nDTEND:20261201T210000Z\r\n", ics);
        Assert.Contains("\r\nDTSTAMP:20261113T090000Z\r\n", ics);
        Assert.Contains(@"SUMMARY:Launch\; party\, \\ now", ics);
        Assert.Contains(@"DESCRIPTION:Line one\nLine two\nBEGIN:VEVENT", ics);
        Assert.Contains(@"LOCATION:Hall A\, 1st floor", ics);
        Assert.Contains("ORGANIZER;CN=\"My Site\":", ics);
        Assert.Contains("STATUS:CONFIRMED", ics);
        // Injected text never becomes its own content line.
        Assert.Equal(1, ics.Split("\r\n").Count(l => l == "BEGIN:VEVENT"));
        Assert.DoesNotContain("\n", ics.Replace("\r\n", ""));
    }

    [Fact]
    public void Build_CancelledStatus()
    {
        var ev = Sample();
        ev.Status = EventStatus.Cancelled;
        Assert.Contains("STATUS:CANCELLED", IcsBuilder.Build(ev, "Site"));
    }

    [Fact]
    public void Fold_LinesAtMost75Octets_AndUnfoldsToOriginal()
    {
        var ev = Sample();
        ev.Description = string.Concat(Enumerable.Repeat("Ünïcödé text ", 30));
        var ics = IcsBuilder.Build(ev, "Site");
        foreach (var line in ics.Split("\r\n"))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line);
        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains("DESCRIPTION:" + IcsBuilder.Escape(ev.Description), unfolded);
    }
}
