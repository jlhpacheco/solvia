using System.Globalization;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class ScheduleSpellingTests
{
    [Theory]
    [InlineData("monay", "monday")]
    [InlineData("tuseday", "tuesday")]
    [InlineData("wendesday", "wednesday")]
    [InlineData("thurday", "thursday")]
    [InlineData("firday", "friday")]
    [InlineData("satuday", "saturday")]
    [InlineData("sundy", "sunday")]
    [InlineData("wensday", "wednesday")]
    [InlineData("wendsday", "wednesday")]
    [InlineData("MONAYS", "MONDAYS")]
    [InlineData("Wensdays", "Wednesdays")]
    [InlineData("Tusedays", "Tuesdays")]
    public void WeekdayTypos_AreUniqueAndKeepCaseAndRecurrence(string text, string expected)
    {
        Assert.Equal(expected, ScheduleSpelling.Normalize(text));
        Assert.False(ScheduleSpelling.HasUnrecognizedScheduleWord(expected));
    }

    [Theory]
    [InlineData("Janaury", "January")]
    [InlineData("Febuary", "February")]
    [InlineData("Mrach", "March")]
    [InlineData("Aprli", "April")]
    [InlineData("May", "May")]
    [InlineData("Juen", "June")]
    [InlineData("Jully", "July")]
    [InlineData("Augst", "August")]
    [InlineData("Septmeber", "September")]
    [InlineData("Octber", "October")]
    [InlineData("Novemeber", "November")]
    [InlineData("Decemeber", "December")]
    [InlineData("NOVEMEBERS", "NOVEMBERS")]
    public void MonthTypos_CoverAllTwelveMonthsWithoutInventingShortMayAliases(string text, string expected)
    {
        Assert.Equal(expected, ScheduleSpelling.Normalize(text));
        Assert.False(ScheduleSpelling.HasUnrecognizedScheduleWord(expected));
    }

    [Theory]
    [InlineData("weeek", "week")]
    [InlineData("wwek", "week")]
    [InlineData("weks", "weeks")]
    [InlineData("weeeks", "weeks")]
    [InlineData("WEEKS", "WEEKS")]
    public void WeekTypos_KeepSingularOrPlural(string text, string expected) =>
        Assert.Equal(expected, ScheduleSpelling.Normalize(text));

    [Fact]
    public void OriginalPunctuationNumericDatesAndNegativeWords_AreUnchanged()
    {
        const string original = "Not Monay, never Tuseday; maybe Wendesday from 10/26/2026 through 2026-11-30 for 3 weks.";
        Assert.Equal("Not Monday, never Tuesday; maybe Wednesday from 10/26/2026 through 2026-11-30 for 3 weeks.",
            ScheduleSpelling.Normalize(original));
        Assert.Equal("Not Monay, never Tuseday; maybe Wendesday from 10/26/2026 through 2026-11-30 for 3 weks.", original);
    }

    [Theory]
    [InlineData("money many maybe never none only once nine")]
    [InlineData("weak seek peek meek leek weed weep geek reek")]
    [InlineData("weeds weeps seeks peeks leeks geeks")]
    [InlineData("sundae sundry tune dune jute jury duly marsh match matches")]
    [InlineData("market marched marcher marchers")]
    [InlineData("today yesterday tomorrow someday birthday holiday payday weekday weekdays weekend weekends everyday")]
    [InlineData("Monday Tuesday Wednesday Thursday Friday Saturday Sunday")]
    [InlineData("January February March April May June July August September October November December")]
    [InlineData("Mon Tues Wed Thur Thurs Fri Sat Sun Jan Feb Mar Apr Jun Jul Aug Sep Sept Oct Nov Dec")]
    [InlineData("monay123 identifier_monay123 2026-10-26 10/26/2026")]
    public void OrdinaryWordsCanonicalWordsIdentifiersAndDates_AreNotCalendarRepairs(string text)
    {
        Assert.Equal(text, ScheduleSpelling.Normalize(text));
        Assert.False(ScheduleSpelling.HasUnrecognizedScheduleWord(text));
    }

    [Theory]
    [InlineData("Jule")]
    [InlineData("Juny")]
    [InlineData("Blursday")]
    [InlineData("Smarch")]
    [InlineData("Funday")]
    [InlineData("Novmbr")]
    public void AmbiguousFictionalOrUnrepairedCalendarWords_AreFlaggedNotGuessed(string text)
    {
        Assert.Equal(text, ScheduleSpelling.Normalize(text));
        Assert.True(ScheduleSpelling.HasUnrecognizedScheduleWord(text));
    }

    [Fact]
    public void MixedKnownAndUnknownWeekday_CannotSilentlyBecomeOnlyMonday()
    {
        var normalized = ScheduleSpelling.Normalize("Monay and Blursday for 3 weks");
        Assert.Equal("Monday and Blursday for 3 weeks", normalized);
        Assert.True(ScheduleSpelling.HasUnrecognizedScheduleWord(normalized));
    }

    [Theory]
    [InlineData("Monay, Wensdays and FIRDAY through Novemeber for three weeeks.")]
    [InlineData("Monday and Blursday in Jule; money, sundae and 10/26/2026 stay unchanged.")]
    [InlineData("Tuesdays and Thursdays for three weeks")]
    public void RepeatedNormalization_IsIdempotent(string text)
    {
        var first = ScheduleSpelling.Normalize(text);
        Assert.Equal(first, ScheduleSpelling.Normalize(first));
    }

    [Theory]
    [InlineData("Monday and Friday as well as Wednesday for three weeks")]
    [InlineData("I need to work each week on Monday and Wednesday for three weeks")]
    [InlineData("I meet each week and walk to campus; keep Monday for three weeks")]
    [InlineData("I have been going weekly on Monday for three weeks")]
    [InlineData("I visit daily for three weeks")]
    public void OrdinaryConnectingWordsAndRecurrenceGrammar_DoNotTriggerCalendarSuspicion(string text)
    {
        var normalized = ScheduleSpelling.Normalize(text);
        Assert.Equal(text, normalized);
        Assert.Equal(normalized, ScheduleSpelling.Normalize(normalized));
        Assert.False(ScheduleSpelling.HasUnrecognizedScheduleWord(normalized));
    }

    [Fact]
    public void RepairsUseInvariantCaseRegardlessOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("FRIDAY in NOVEMBER", ScheduleSpelling.Normalize("FIRDAY in NOVEMEBER"));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void EmptyOrLongUnrelatedWords_DoNotCreateCalendarFacts()
    {
        Assert.Equal("", ScheduleSpelling.Normalize(""));
        Assert.False(ScheduleSpelling.HasUnrecognizedScheduleWord(""));
        var word = new string('x', 10_000);
        Assert.Equal(word, ScheduleSpelling.Normalize(word));
        Assert.False(ScheduleSpelling.HasUnrecognizedScheduleWord(word));
    }
}
