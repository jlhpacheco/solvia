using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;
using Solvia.Models.Enums;

namespace Solvia.Tests;

public sealed partial class CheckoutTests
{
    [Theory]
    [InlineData("active", PaymentStatus.Paid, true)]
    [InlineData("expired", PaymentStatus.Paid, false)]
    [InlineData("future", PaymentStatus.Paid, false)]
    [InlineData("denied", PaymentStatus.Paid, false)]
    [InlineData("future", PaymentStatus.NotRequired, false)]
    public async Task SavedReceiptAndPermitList_AgreeWithPublicValidityWithoutLosingHistory(
        string state, PaymentStatus payment, bool active)
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var client = factory.CreateClient();
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
            var application = AddApplication(db, payment: payment, orderId: "OFFLINE-ORDER");
            var today = DateOnly.FromDateTime(DateTime.Today);
            application.StartDate = state == "future" ? today.AddDays(1) : today.AddDays(-2);
            application.EndDate = state == "expired" ? today.AddDays(-1) : today.AddDays(2);
            if (state == "denied") application.Status = ApplicationStatus.Denied;
            application.ReceiptNumber = "VALIDITY-HISTORY";
            application.PayPalCaptureId = payment == PaymentStatus.Paid ? "OFFLINE-CAPTURE" : null;
            await db.SaveChangesAsync();
            id = application.Id;
        }
        var receipt = await client.GetStringAsync($"/Permits/Receipt/{id}");
        Assert.Contains("VALIDITY-HISTORY", receipt);
        Assert.Contains("Download permit (PDF)", receipt);
        var list = await client.GetStringAsync("/Permits/My");
        if (active)
        {
            Assert.Contains("Permit live", receipt);
            Assert.DoesNotContain("Issued · inactive", list);
        }
        else
        {
            Assert.DoesNotContain("Permit live", receipt);
            Assert.DoesNotContain(">Live</span>", list);
            Assert.Contains("Issued · inactive", list);
            Assert.Contains(payment == PaymentStatus.Paid ? "Payment confirmed" : "Permit issued", receipt);
        }
        if (payment == PaymentStatus.Paid)
        {
            Assert.Contains("OFFLINE-CAPTURE", receipt);
            Assert.Contains("Paid", list);
        }
    }
}
