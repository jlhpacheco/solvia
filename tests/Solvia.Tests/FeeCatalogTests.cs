using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R1 fee-catalog tests. Every expected number below comes straight
/// from the frozen research file data/fees.csv — the parser must
/// reproduce the campuses' real published fees exactly, return null
/// where a campus publishes no price, treat a published FREE as a
/// real 0, and parse the day-pass column into DayPassCents without
/// mistaking an hourly rate for the day rate.
/// </summary>
public sealed class FeeCatalogTests
{
    private static readonly FeeCatalogService Catalog = new(RedoTestDb.FindFeesCsv());

    private CampusFee FindOrFail(string campus)
    {
        var fee = Catalog.Find(campus);
        Assert.NotNull(fee);
        return fee!;
    }

    [Fact]
    public void LoadsExactly10Campuses()
    {
        Assert.Equal(10, Catalog.All().Count);
    }

    [Fact]
    public void EveryCampusPublishesASemesterPrice()
    {
        // The shipped table holds ONLY the campuses that publish a
        // student semester price (FREE counts) — the scanned campuses
        // that publish none were removed by Jose Luis's order.
        Assert.Equal(
            Catalog.All().Count,
            Catalog.All().Count(c => c.HasPublishedSemesterPrice));
    }

    [Fact]
    public void CsuMontereyBay_SemesterAndDayPass()
    {
        var fee = FindOrFail("CSU Monterey Bay");
        Assert.Equal(22950, Catalog.SemesterPriceCents(fee, TermKind.Fall));
        Assert.Equal(22950, Catalog.SemesterPriceCents(fee, TermKind.Spring));
        Assert.Equal(15300, Catalog.SemesterPriceCents(fee, TermKind.Summer));
        Assert.False(fee.IsFree);
        // "$5 Student/Other; $2 Staff…" — the student-facing segment wins.
        Assert.Equal(500, fee.DayPassCents);
    }

    [Fact]
    public void CsuEastBay_DayPassPrefersTheDayRateOverTheHourlyRate()
    {
        var fee = FindOrFail("CSU East Bay");
        Assert.Equal(19500, Catalog.SemesterPriceCents(fee, TermKind.Fall));
        // "$2/hour or $10/day (dispensers)" — the day pass is $10,
        // not the $2 hourly rate sitting in front of it.
        Assert.Equal(1000, fee.DayPassCents);
    }

    [Fact]
    public void CsuBakersfield_DayPassIs600()
    {
        var fee = FindOrFail("CSU Bakersfield");
        Assert.Equal(9300, Catalog.SemesterPriceCents(fee, TermKind.Fall));
        // "$6/day (on-campus ticket dispensers)".
        Assert.Equal(600, fee.DayPassCents);
    }

    [Fact]
    public void FresnoCity_SemesterAndDayPass()
    {
        var fee = FindOrFail("Fresno City College");
        Assert.Equal(3000, Catalog.SemesterPriceCents(fee, TermKind.Fall));
        Assert.Equal(3000, Catalog.SemesterPriceCents(fee, TermKind.Spring));
        Assert.Equal(2000, Catalog.SemesterPriceCents(fee, TermKind.Summer));
        Assert.Equal(100, fee.DayPassCents);
    }

    [Fact]
    public void YccdIsFree_RealZero_NotNull_AndDayPass200()
    {
        var fee = FindOrFail("Modesto Junior College / Columbia College / YCCD");
        Assert.True(fee.IsFree);

        var price = Catalog.SemesterPriceCents(fee, TermKind.Fall);
        Assert.True(price.HasValue); // a real published price…
        Assert.Equal(0, price.Value); // …of exactly zero — distinct from null
        Assert.Equal(200, fee.DayPassCents);
    }

    [Fact]
    public void NotListedCampus_AllPricesNull_NotZero()
    {
        // Every campus in the SHIPPED table publishes a price now, so
        // the not-listed behavior is proven against a synthetic row:
        // the parser must still map "not listed" to null — never 0 —
        // for semester AND day-pass prices alike.
        var header = File.ReadLines(RedoTestDb.FindFeesCsv()).First();
        var row = "Nowhere State,not listed on PMB page,not listed on PMB page," +
                  "not listed on PMB page,not listed on PMB page,not listed on PMB page," +
                  "not listed on PMB page,synthetic test row";
        var path = Path.Combine(
            Path.GetTempPath(), $"solvia-fees-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, header + "\n" + row + "\n");
        try
        {
            var catalog = new FeeCatalogService(path);
            var fee = catalog.Find("Nowhere State");
            Assert.NotNull(fee);
            Assert.False(fee!.IsFree);
            Assert.Null(catalog.SemesterPriceCents(fee, TermKind.Fall));
            Assert.Null(catalog.SemesterPriceCents(fee, TermKind.Spring));
            Assert.Null(catalog.SemesterPriceCents(fee, TermKind.Summer));
            Assert.Null(fee.DayPassCents);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FindUnknownCampusReturnsNull()
    {
        Assert.Null(Catalog.Find("Hogwarts School of Parking"));
    }

    [Fact]
    public void FindIsCaseInsensitiveAndTrimsWhitespace()
    {
        Assert.NotNull(Catalog.Find("  fresno city college  "));
        Assert.NotNull(Catalog.Find("CSU MONTEREY BAY"));
    }
}
