using CseForms.Application.Common;

namespace CseForms.Application.Tests;

public class CseCalendarTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData("2026-12-31T23:30:00Z", "2027-01-01")] // 00h30 à Paris en hiver (UTC+1), encore le 31 en UTC
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01")] // minuit pile à Paris
    [InlineData("2026-12-31T22:59:59Z", "2026-12-31")] // 23h59 à Paris
    [InlineData("2026-06-30T22:30:00Z", "2026-07-01")] // 00h30 à Paris en été (UTC+2), encore le 30 en UTC
    [InlineData("2026-06-30T21:30:00Z", "2026-06-30")]
    public void Today_is_computed_in_paris_time_zone(string utc, string expected)
    {
        var calendar = new CseCalendar(new FixedTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), calendar.Today);
    }
}

public class CseCalendarLocalTimeTests
{
    [Theory]
    [InlineData("2026-12-01T08:00", "2026-12-01T07:00:00Z")] // hiver, UTC+1
    [InlineData("2026-07-01T08:00", "2026-07-01T06:00:00Z")] // été, UTC+2
    [InlineData("2026-10-25T02:30", "2026-10-25T01:30:00Z")] // heure ambiguë du passage à l'hiver : lue en heure d'hiver
    public void Paris_time_is_converted_to_utc(string local, string expected)
    {
        var utc = CseCalendar.LocalToUtc(DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), utc);
        Assert.Equal(TimeSpan.Zero, utc!.Value.Offset);
    }

    [Fact]
    public void Missing_hour_of_the_spring_change_is_refused()
    {
        Assert.Null(CseCalendar.LocalToUtc(new DateTime(2026, 3, 29, 2, 30, 0)));
    }
}
