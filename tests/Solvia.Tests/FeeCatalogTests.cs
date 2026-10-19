using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// T1 fee-catalog tests. Every expected number below comes straight from
/// the frozen research file data/fees.csv — the parser must reproduce
/// the campuses' real published fees exactly, return null where a campus
/// publishes no price, and treat a published FREE as a real 0.
/// </summary>
public sealed class FeeCatalogTests
{
    private static readonly FeeCatalogService Catalog = new(FindFeesCsv());

    private static string FindFeesCsv()
    {
        // Start from the test assembly's own folder (AppContext.BaseDirectory
        // is the test RUNNER's folder under some runners, not the test bin).
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(FeeCatalogTests).Assembly.Location),
            AppContext.BaseDirectory,
        };

        foreach (var start in starts)
        {
            var dir = start is null ? null : new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "fees.csv");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException(
            "Could not locate data/fees.csv above the test assembly.");
    }

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
        // Jose Luis's Oct 18 order: the shipped table holds ONLY the
        // campuses that publish a student semester price (FREE counts)
        // — the 15 scanned campuses that publish none were removed.
        Assert.Equal(
            Catalog.All().Count,
            Catalog.All().Count(c => c.HasPublishedSemesterPrice));
    }

    [Fact]
    public void CsuMontereyBayFallSemesterIs22950Cents()
    {
        var fee = FindOrFail("CSU Monterey Bay");
        Assert.Equal(22950, Catalog.SemesterPriceCents(fee, TermKind.Fall));
        Assert.Equal(22950, Catalog.SemesterPriceCents(fee, TermKind.Spring));
        Assert.Equal(15300, Catalog.SemesterPriceCents(fee, TermKind.Summer));
        Assert.False(fee.IsFree);
    }

    [Fact]
    public void FresnoCitySemesterIs3000Cents()
    {
        var fee = FindOrFail("Fresno City College");
        Assert.Equal(3000, Catalog.SemesterPriceCents(fee, TermKind.Fall));
        Assert.Equal(3000, Catalog.SemesterPriceCents(fee, TermKind.Spring));
        Assert.Equal(2000, Catalog.SemesterPriceCents(fee, TermKind.Summer));
    }

    [Fact]
    public void YccdIsFree_RealZero_NotNull()
    {
        var fee = FindOrFail("Modesto Junior College / Columbia College / YCCD");
        Assert.True(fee.IsFree);

        var price = Catalog.SemesterPriceCents(fee, TermKind.Fall);
        Assert.True(price.HasValue); // a real published price…
        Assert.Equal(0, price.Value); // …of exactly zero — distinct from null
    }

    [Fact]
    public void NotListedCampusReturnsNull_NotZero()
    {
        // Every campus in the SHIPPED table publishes a price now, so
        // the not-listed behavior is proven against a synthetic row:
        // the parser must still map "not listed" to null — never 0 —
        // so the defensive path stays honest for any future data.
        var header = File.ReadLines(FindFeesCsv()).First();
        var row = "Nowhere State,not listed on PMB page,not listed on PMB page," +
                  "$5/day,not listed on PMB page,not listed on PMB page," +
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
