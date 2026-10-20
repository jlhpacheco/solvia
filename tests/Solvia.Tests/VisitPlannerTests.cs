using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class VisitPlannerTests
{
    private static readonly DayOfWeek[] TuesdayThursday = [DayOfWeek.Tuesday, DayOfWeek.Thursday];
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    [Fact]
    public void ThreeRollingWeeks_EnumerateSixActualDatesAndCompareIntegerCents()
    {
        var result = Service().Compare(Request(), UserCategory.Student);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { new DateOnly(2026, 10, 20), new(2026, 10, 22), new(2026, 10, 27),
            new(2026, 10, 29), new(2026, 11, 3), new(2026, 11, 5) }, result.VisitDates);
        Assert.Equal(6, result.DayCount);
        Assert.Equal(500, result.DayCents);
        Assert.Equal(3000, result.DailyTotal);
        Assert.Equal(22950, result.TermCents);
        Assert.Equal("Daily passes", result.Recommendation);
        Assert.Equal(19950, result.SavingsCents);
        Assert.Equal(46, result.BreakEvenDays);
        Assert.Contains("snapshot", result.FeeAsOf);
        Assert.Contains("fictional catalog source", result.FeeSource);
        Assert.Contains("only the first planned day", result.Disclosure);
    }

    [Fact]
    public void FiftyVisits_MakeTermCheaperWithoutChangingThePublishedAmounts()
    {
        var result = Service().Compare(Request() with { StartDate = new(2026, 8, 24), Weeks = 10, Weekdays = Weekdays }, UserCategory.Public);
        Assert.True(result.IsValid);
        Assert.Equal(50, result.DayCount);
        Assert.Equal(25000, result.DailyTotal);
        Assert.Equal(22950, result.TermCents);
        Assert.Equal("Term permit", result.Recommendation);
        Assert.Equal(2050, result.SavingsCents);
    }

    [Fact]
    public void ExactBreakEven_IsATieRatherThanAnInventedSaving()
    {
        var result = Service(day: 100, term: 3000).Compare(Request() with { StartDate = new(2026, 8, 24), Weeks = 6, Weekdays = Weekdays }, UserCategory.Public);
        Assert.Equal(30, result.DayCount);
        Assert.Equal("Tie", result.Recommendation);
        Assert.Equal(0, result.SavingsCents);
        Assert.Equal(30, result.BreakEvenDays);
    }

    [Theory]
    [InlineData(UserCategory.Student)]
    [InlineData(UserCategory.Public)]
    [InlineData(UserCategory.Visitor)]
    [InlineData(UserCategory.Faculty)]
    public void NonEmployeeCategoriesUsePublishedPrices(UserCategory category)
    {
        var result = Service(day: 100, term: 3000).Compare(Request(), category);
        Assert.Equal(category, result.Category);
        Assert.Equal(100, result.DayCents);
        Assert.Equal(600, result.DailyTotal);
        Assert.Equal(3000, result.TermCents);
    }

    [Fact]
    public void EmployeeExemptionAppliesOnlyAfterBothPricesArePublished()
    {
        var result = Service().Compare(Request(), UserCategory.Employee);
        Assert.True(result.IsValid);
        Assert.Equal(0, result.DayCents);
        Assert.Equal(0, result.DailyTotal);
        Assert.Equal(0, result.TermCents);
        Assert.Equal("Tie", result.Recommendation);
        Assert.Null(result.BreakEvenDays);
        Assert.Contains("exemption", result.Reason);
    }

    [Theory]
    [InlineData(UserCategory.Student)]
    [InlineData(UserCategory.Employee)]
    public void MissingDayPriceRemainsNull_EvenForEmployees(UserCategory category)
    {
        var result = Service(day: null).Compare(Request(), category);
        Assert.True(result.IsValid);
        Assert.Null(result.DayCents);
        Assert.Null(result.DailyTotal);
        Assert.True(result.TermAvailable);
        Assert.Equal(category == UserCategory.Employee ? 0 : 22950, result.TermCents);
        Assert.Equal("Unavailable", result.Recommendation);
        Assert.Null(result.SavingsCents);
        Assert.Null(result.BreakEvenDays);
    }

    [Theory]
    [InlineData(UserCategory.Student)]
    [InlineData(UserCategory.Employee)]
    public void MissingTermPriceRemainsNull_EvenForEmployees(UserCategory category)
    {
        var result = Service(term: null).Compare(Request(), category);
        Assert.True(result.IsValid);
        Assert.False(result.TermAvailable);
        Assert.Null(result.TermCents);
        Assert.NotNull(result.DailyTotal);
        Assert.Equal("Unavailable", result.Recommendation);
    }

    [Fact]
    public void PublishedFreeTerm_IsARealZeroWithAOneVisitThreshold()
    {
        var result = Service(day: 200, term: 0).Compare(Request(), UserCategory.Public);
        Assert.Equal(0, result.TermCents);
        Assert.Equal(1200, result.DailyTotal);
        Assert.Equal("Term permit", result.Recommendation);
        Assert.Equal(1, result.BreakEvenDays);
    }

    [Fact]
    public void PublishedFreeDailyPrice_HasNoDivisionByZeroBreakEven()
    {
        var result = Service(day: 0, term: 1000).Compare(Request(), UserCategory.Student);
        Assert.Equal(0, result.DailyTotal);
        Assert.Equal("Daily passes", result.Recommendation);
        Assert.Null(result.BreakEvenDays);
    }

    [Fact]
    public void DatesUseAnchoredPermitEnd_AndAllowVisitsAfterAcademicEndWithinThatWindow()
    {
        var result = Service().Compare(Request() with { StartDate = new(2026, 12, 8), Weeks = 3, Weekdays = [DayOfWeek.Tuesday] }, UserCategory.Student);
        Assert.True(result.IsValid);
        Assert.Equal(new DateOnly(2026, 12, 22), result.LastVisit);
        Assert.Equal(new DateOnly(2026, 12, 22), result.Window!.PermitEndDate);
    }

    [Fact]
    public void CrossingActualPermitEnd_IsRejectedNotGivenANew120DayWindow()
    {
        var result = Service().Compare(Request() with { StartDate = new(2026, 12, 8), Weeks = 3 }, UserCategory.Student);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("crosses"));
        Assert.Empty(result.VisitDates);
        Assert.Null(result.DailyTotal);
    }

    [Fact]
    public void FirstVisitAfterAcademicTermEnd_IsNotAnEligibleTermComparison()
    {
        var result = Service().Compare(Request() with { StartDate = new(2026, 12, 15), Weeks = 1 }, UserCategory.Student);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("first visit"));
    }

    [Fact]
    public void ScheduleAnchorCanPrecedeTerm_IfEveryActualVisitIsCovered()
    {
        var result = Service().Compare(Request() with { StartDate = new(2026, 8, 23), Weeks = 1, Weekdays = [DayOfWeek.Monday] }, UserCategory.Student);
        Assert.True(result.IsValid);
        Assert.Equal(new DateOnly(2026, 8, 24), result.FirstVisit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(27)]
    [InlineData(int.MaxValue)]
    public void InvalidWeekBoundsGiveReviewErrors(int weeks)
    {
        var result = Service().Compare(Request() with { Weeks = weeks }, UserCategory.Student);
        Assert.False(result.IsValid);
        Assert.Null(result.DailyTotal);
    }

    [Fact]
    public void EmptyDuplicateAndUndefinedWeekdaysAreRejected()
    {
        foreach (var days in new DayOfWeek[][] { [], [DayOfWeek.Tuesday, DayOfWeek.Tuesday], [(DayOfWeek)99] })
        {
            var result = Service().Compare(Request() with { Weekdays = days }, UserCategory.Student);
            Assert.False(result.IsValid);
            Assert.Empty(result.VisitDates);
        }
    }

    [Fact]
    public void UnknownCampusCategoryAndTermCannotInventAComparison()
    {
        Assert.False(Service().Compare(Request() with { Campus = "Invented College" }, UserCategory.Student).IsValid);
        Assert.False(Service().Compare(Request(), (UserCategory)99).IsValid);
        Assert.False(Service().Compare(Request() with { Term = (TermKind)99 }, UserCategory.Student).IsValid);
    }

    [Fact]
    public void PastStartDateAndCalendarOverflowReturnErrors()
    {
        Assert.False(Service().Compare(Request() with { StartDate = new(2025, 12, 31) }, UserCategory.Student).IsValid);
        var overflow = Service().Compare(Request() with { StartDate = DateOnly.MaxValue, Weeks = 2 }, UserCategory.Student);
        Assert.False(overflow.IsValid);
        Assert.Contains(overflow.Errors, error => error.Contains("calendar"));
    }

    [Fact]
    public void IntegerMoneyOverflowAndNegativeCatalogDataAreRejected()
    {
        var overflow = Service(day: int.MaxValue).Compare(Request(), UserCategory.Student);
        Assert.False(overflow.IsValid);
        Assert.Contains(overflow.Errors, error => error.Contains("too large"));
        Assert.Null(overflow.DailyTotal);
        Assert.False(Service(day: -1).Compare(Request(), UserCategory.Employee).IsValid);
        Assert.False(Service(term: -1).Compare(Request(), UserCategory.Student).IsValid);
    }

    [Fact]
    public void MissingTermDatesCannotBeAssumedEligible()
    {
        var catalog = new Catalog(500, 22950);
        var planner = new VisitPlannerService(catalog, new PricingService(catalog), new Dates(null), new Clock());
        var result = planner.Compare(Request(), UserCategory.Student);
        Assert.False(result.IsValid);
        Assert.False(result.TermAvailable);
        Assert.Null(result.TermCents);
    }

    private static PlanRequest Request() => new("Test College", new(2026, 10, 20), 3, TuesdayThursday, TermKind.Fall);
    private static VisitPlannerService Service(int? day = 500, int? term = 22950)
    {
        var catalog = new Catalog(day, term);
        var dates = new TermDates("Test College", "Fall", 2026, new(2026, 8, 24), new(2026, 12, 11), new(2026, 12, 22), new(2026, 12, 22), 120);
        return new(catalog, new PricingService(catalog), new Dates(dates), new Clock());
    }

    private sealed class Catalog(int? day, int? term) : IFeeCatalog
    {
        private readonly CampusFee _fee = new("Test College", "fixture", "fixture", "fixture", "", term, term, term, term == 0, "fictional catalog source for tests") { DayPassCents = day };
        public CampusFee? Find(string campusName) => campusName.Trim().Equals(_fee.Campus, StringComparison.OrdinalIgnoreCase) ? _fee : null;
        public IReadOnlyList<CampusFee> All() => [_fee];
        public int? SemesterPriceCents(CampusFee fee, TermKind termKind) => term;
    }
    private sealed class Dates(TermDates? dates) : ITermDatesService
    {
        public TermDates? GetTermDates(string campusName, TermKind term, int year) => dates;
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
