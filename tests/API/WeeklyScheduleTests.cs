using UAI.ZeroVP.API.Scheduling;

namespace UAI.ZeroVP.Tests.API;

/// <summary>Výpočet termínů týdně se opakujícího úkolu.</summary>
public sealed class WeeklyScheduleTests
{
    private static readonly DateTimeOffset FirstDue = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetDueDates_RepeatCountThree_ReturnsFourWeeklyDates()
    {
        var dueDates = WeeklySchedule.GetDueDates(FirstDue, repeatCount: 3);

        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero)
            ],
            dueDates);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(52)]
    public void GetDueDates_ReturnsFirstOccurrencePlusRepeatCount(int repeatCount)
    {
        var dueDates = WeeklySchedule.GetDueDates(FirstDue, repeatCount);

        Assert.Equal(repeatCount + 1, dueDates.Count);
        Assert.Equal(FirstDue, dueDates[0]);
        Assert.Equal(FirstDue.AddDays(7 * repeatCount), dueDates[^1]);
    }

    [Fact]
    public void GetDueDates_ConsecutiveDates_AreExactlySevenDaysApart()
    {
        var dueDates = WeeklySchedule.GetDueDates(FirstDue, repeatCount: 52);

        Assert.All(
            dueDates.Zip(dueDates.Skip(1)),
            pair => Assert.Equal(TimeSpan.FromDays(7), pair.Second - pair.First));
    }

    [Fact]
    public void GetDueDates_AcrossDaylightSavingChange_KeepsSameUtcTime()
    {
        // V noci na 25. 10. 2026 končí v Evropě letní čas. V UTC se to nesmí projevit.
        var beforeChange = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

        var dueDates = WeeklySchedule.GetDueDates(beforeChange, repeatCount: 1);

        Assert.Equal(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), dueDates[1]);
    }

    [Fact]
    public void GetDueDates_NonUtcOffset_IsConvertedToUtc()
    {
        var pragueTime = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(1));

        var dueDates = WeeklySchedule.GetDueDates(pragueTime, repeatCount: 1);

        Assert.All(dueDates, dueDate => Assert.Equal(TimeSpan.Zero, dueDate.Offset));
        Assert.Equal(FirstDue, dueDates[0]);
    }

    [Fact]
    public void GetDueDates_ZeroRepeatCount_ReturnsOnlyFirstOccurrence()
    {
        Assert.Equal([FirstDue], WeeklySchedule.GetDueDates(FirstDue, repeatCount: 0));
    }

    [Fact]
    public void GetDueDates_NegativeRepeatCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WeeklySchedule.GetDueDates(FirstDue, repeatCount: -1));
    }
}