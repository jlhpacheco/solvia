using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R1 pricing tests (REDO-PLAN §2a/§2b). The fee catalog is the only
/// price truth; the PermitType row's PriceCents is a seeded-0
/// placeholder that must never leak into a price; the Employee
/// exemption applies AFTER resolution, so an unpublished fee blocks
/// everyone alike.
/// </summary>
public sealed class PricingTests
{
    private static readonly FeeCatalogService Catalog = new(RedoTestDb.FindFeesCsv());
    private static readonly PricingService Pricing = new(Catalog);

    private CampusFee FindOrFail(string campus)
    {
        var fee = Catalog.Find(campus);
        Assert.NotNull(fee);
        return fee!;
    }

    private static FeeCatalogService SyntheticCatalog(string row)
    {
        var header = File.ReadLines(RedoTestDb.FindFeesCsv()).First();
        var path = Path.Combine(
            Path.GetTempPath(), $"solvia-pricing-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, header + "\n" + row + "\n");
        return new FeeCatalogService(path);
    }

    private const string UnpublishedRow =
        "Nowhere State,not listed on PMB page,not listed on PMB page," +
        "not listed on PMB page,not listed on PMB page,not listed on PMB page," +
        "not listed on PMB page,synthetic test row";

    // ------------------------------------------------------------ guard

    [Fact]
    public void Guard_FresnoTerm_Prices3000FromCatalog_WhileTypeRowSitsAtZero()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;

        // The non-authoritative placeholder, proven to be 0 in the DB…
        var termRow = db.PermitTypes.Single(t => t.Code == PermitTypeCode.Term);
        Assert.Equal(0, termRow.PriceCents);

        // …while the price anyone pays comes from the catalog alone.
        var fresno = FindOrFail("Fresno City College");
        Assert.Equal(3000, Pricing.ResolvePriceCents(fresno, PermitTypeCode.Term, TermKind.Fall));
    }

    // ------------------------------------------------------- resolution

    [Fact]
    public void FresnoTerm_Resolves3000_ForEveryNonExemptCategory()
    {
        var fresno = FindOrFail("Fresno City College");
        Assert.Equal(3000, Pricing.ResolvePriceCents(fresno, PermitTypeCode.Term, TermKind.Fall, UserCategory.Student));
        Assert.Equal(3000, Pricing.ResolvePriceCents(fresno, PermitTypeCode.Term, TermKind.Fall, UserCategory.Public));
        // Faculty and Visitor price exactly like Public (§2b).
        Assert.Equal(3000, Pricing.ResolvePriceCents(fresno, PermitTypeCode.Term, TermKind.Fall, UserCategory.Faculty));
        Assert.Equal(3000, Pricing.ResolvePriceCents(fresno, PermitTypeCode.Term, TermKind.Fall, UserCategory.Visitor));
    }

    [Fact]
    public void YccdTerm_ResolvesARealZero()
    {
        var yccd = FindOrFail("Modesto Junior College / Columbia College / YCCD");
        var price = Pricing.ResolvePriceCents(yccd, PermitTypeCode.Term, TermKind.Fall);
        Assert.True(price.HasValue);
        Assert.Equal(0, price.Value);
    }

    [Theory]
    [InlineData(UserCategory.Student)]
    [InlineData(UserCategory.Public)]
    public void YccdFreeTerm_DoesNotZeroTheSeparateCatalogDayPass(UserCategory category)
    {
        var yccd = FindOrFail("Modesto Junior College / Columbia College / YCCD");
        Assert.Equal(0, Pricing.ResolvePriceCents(yccd, PermitTypeCode.Term, TermKind.Fall, category));
        Assert.Equal(200, Pricing.ResolvePriceCents(yccd, PermitTypeCode.Daily, TermKind.Fall, category));
    }

    [Fact]
    public void BakersfieldDaily_Resolves600FromTheDayPass()
    {
        var bakersfield = FindOrFail("CSU Bakersfield");
        Assert.Equal(600, Pricing.ResolvePriceCents(
            bakersfield, PermitTypeCode.Daily, TermKind.Fall));
    }

    [Fact]
    public void UnpublishedTerm_ResolvesNull_BlockedNotInvented()
    {
        var catalog = SyntheticCatalog(UnpublishedRow);
        var pricing = new PricingService(catalog);
        var fee = catalog.Find("Nowhere State");
        Assert.NotNull(fee);
        Assert.Null(pricing.ResolvePriceCents(fee!, PermitTypeCode.Term, TermKind.Fall));
    }

    [Fact]
    public void UnpublishedDayPass_ResolvesNull_BlockedNotInvented()
    {
        var catalog = SyntheticCatalog(UnpublishedRow);
        var pricing = new PricingService(catalog);
        var fee = catalog.Find("Nowhere State");
        Assert.NotNull(fee);
        Assert.Null(fee!.DayPassCents);
        Assert.Null(pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, TermKind.Fall));
    }

    [Fact]
    public void UnknownCampus_ResolvesNull()
    {
        Assert.Null(Pricing.ResolvePriceCents(
            "Hogwarts School of Parking", PermitTypeCode.Term, TermKind.Fall,
            UserCategory.Student));
    }

    // -------------------------------------------------- employee exempt

    [Fact]
    public void Employee_ResolvesZero_AfterTheFeeWasFound()
    {
        var fresno = FindOrFail("Fresno City College");
        Assert.Equal(0, Pricing.ResolvePriceCents(
            fresno, PermitTypeCode.Term, TermKind.Fall, UserCategory.Employee));
        // …for a published day pass too.
        var bakersfield = FindOrFail("CSU Bakersfield");
        Assert.Equal(0, Pricing.ResolvePriceCents(
            bakersfield, PermitTypeCode.Daily, TermKind.Fall, UserCategory.Employee));
    }

    [Fact]
    public void Employee_DoesNotUnblockAnUnpublishedFee()
    {
        // The catalog is consulted FIRST (§2a): an unpublished price
        // blocks even an employee — there is no silent free issue.
        var catalog = SyntheticCatalog(UnpublishedRow);
        var pricing = new PricingService(catalog);
        var fee = catalog.Find("Nowhere State");
        Assert.NotNull(fee);
        Assert.Null(pricing.ResolvePriceCents(
            fee!, PermitTypeCode.Term, TermKind.Fall, UserCategory.Employee));
        Assert.Null(pricing.ResolvePriceCents(
            fee!, PermitTypeCode.Daily, TermKind.Fall, UserCategory.Employee));
    }
}
