using WorkTime.Web.Domain;

namespace WorkTime.Tests;

public class TimeEntryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static TimeEntry Entry(int startMinutes, int? endMinutes) => new()
    {
        Description = "Задача",
        StartedAt = T0.AddMinutes(startMinutes),
        EndedAt = endMinutes is null ? null : T0.AddMinutes(endMinutes.Value),
    };

    [Theory]
    [InlineData(10, 20, 0, 60, 600)]    // целиком внутри периода
    [InlineData(-10, 20, 0, 60, 1200)]  // начало до периода
    [InlineData(50, 70, 0, 60, 600)]    // конец после периода
    [InlineData(-10, 70, 0, 60, 3600)]  // шире периода
    [InlineData(-20, -10, 0, 60, 0)]    // раньше периода
    [InlineData(70, 80, 0, 60, 0)]      // позже периода
    public void Counts_only_overlap_with_period(int start, int end, int from, int to, long expected)
    {
        var seconds = Entry(start, end).SecondsWithin(T0.AddMinutes(from), T0.AddMinutes(to), T0.AddMinutes(1000));

        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData(30, 600)]    // идёт и ещё не дошёл до конца периода
    [InlineData(1000, 3000)] // идёт дольше конца периода
    [InlineData(-5, 0)]      // «сейчас» раньше начала записи
    public void Running_entry_lasts_until_now(int nowMinutes, long expected)
    {
        var seconds = Entry(20, null).SecondsWithin(T0, T0.AddMinutes(70), T0.AddMinutes(nowMinutes));

        Assert.Equal(expected, seconds);
    }

    [Fact]
    public void Fractions_of_second_are_dropped()
    {
        var entry = new TimeEntry { Description = "Задача", StartedAt = T0, EndedAt = T0.AddSeconds(1.9) };

        Assert.Equal(1, entry.SecondsWithin(T0, T0.AddHours(1), T0.AddHours(1)));
    }
}
