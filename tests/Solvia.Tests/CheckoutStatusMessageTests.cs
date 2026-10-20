using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Solvia.Data;
using Solvia.Models.Enums;

namespace Solvia.Tests;

public sealed partial class CheckoutTests
{
    [Theory]
    [InlineData("active", PaymentStatus.Paid, true, "this permit is active")]
    [InlineData("denied", PaymentStatus.Paid, false, "was denied")]
    [InlineData("future", PaymentStatus.Paid, false, "not valid yet")]
    [InlineData("expired", PaymentStatus.Paid, false, "ended on")]
    [InlineData("future", PaymentStatus.NotRequired, false, "not valid yet")]
    public async Task DoneRenderedReceipt_SeparatesPaymentFromCurrentPermitValidity(
        string state, PaymentStatus payment, bool active, string expected)
    {
        using var factory = new AppIntegrationTests.DemoFactory();
        using var client = factory.CreateClient();
        Guid applicationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
            var application = AddApplication(db, payment: payment, orderId: "OFFLINE-ORDER");
            var today = DateOnly.FromDateTime(DateTime.Today);
            application.StartDate = state == "future" ? today.AddDays(1) : today.AddDays(-2);
            application.EndDate = state == "expired" ? today.AddDays(-1) : today.AddDays(2);
            if (state == "denied") application.Status = ApplicationStatus.Denied;
            application.ReceiptNumber = "OFFLINE-RECEIPT";
            application.PayPalCaptureId = payment == PaymentStatus.Paid ? "OFFLINE-CAPTURE" : null;
            await db.SaveChangesAsync();
            applicationId = application.Id;
        }

        using var response = await client.GetAsync($"/Checkout/Done/{applicationId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("OFFLINE-RECEIPT", html);
        Assert.Contains(expected, html);
        if (active)
            Assert.Contains("Permit live", html);
        else
        {
            Assert.DoesNotContain("Permit live", html);
            Assert.DoesNotContain("this permit is active", html);
            Assert.Contains(payment == PaymentStatus.Paid ? "Payment confirmed" : "Permit issued", html);
        }
        if (payment == PaymentStatus.Paid)
        {
            Assert.Contains("OFFLINE-ORDER", html);
            Assert.Contains("OFFLINE-CAPTURE", html);
        }
    }

    [Theory]
    [InlineData("active")]
    [InlineData("denied")]
    [InlineData("future")]
    [InlineData("expired")]
    public async Task StaleCancel_AfterCompletedCapture_ShowsPaidReceiptWithoutAnotherCapture(string validity)
    {
        var world = NewWorld();
        using var connection = world.Connection;
        using var db = world.Db;
        var application = AddApplication(world.Db, orderId: "ORDER-1");
        var capture = await world.Checkout.CaptureAsync(application.Id);
        Assert.True(capture.Completed);
        var today = DateOnly.FromDateTime(DateTime.Today);
        application.StartDate = validity == "future" ? today.AddDays(1) : today.AddDays(-2);
        application.EndDate = validity == "expired" ? today.AddDays(-1) : today.AddDays(2);
        if (validity == "denied") application.Status = ApplicationStatus.Denied;
        await world.Db.SaveChangesAsync();
        var controller = NewCheckoutController(world, "maria");

        var result = await controller.Cancel("ORDER-1", default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Done", redirect.ActionName);
        Assert.Equal(application.Id, redirect.RouteValues!["id"]);
        Assert.Contains("already confirmed", controller.TempData["StatusMessage"] as string);
        Assert.DoesNotContain("permit is live", controller.TempData["StatusMessage"] as string);
        Assert.Contains("validity below", controller.TempData["StatusMessage"] as string);
        Assert.False(controller.TempData.ContainsKey("CheckoutInfo"));
        var saved = await Reload(world, application.Id);
        Assert.Equal(PaymentStatus.Paid, saved.PaymentStatus);
        Assert.Equal(capture.CaptureId, saved.PayPalCaptureId);
        Assert.Equal(capture.ReceiptNumber, saved.ReceiptNumber);
        Assert.Equal(1, world.PayPal.CaptureCalls);
    }

    [Fact]
    public async Task UnknownReturn_DoesNotClaimProviderFundsWereNeverCaptured()
    {
        var world = NewWorld();
        using var connection = world.Connection;
        using var db = world.Db;
        var application = AddApplication(world.Db, orderId: "ORDER-1");
        await world.Checkout.CaptureAsync(application.Id);
        var controller = NewCheckoutController(world, "maria");

        var result = await controller.Return("different-or-stale-token", default);

        Assert.IsType<RedirectResult>(result);
        var message = Assert.IsType<string>(controller.TempData["ErrorMessage"]);
        Assert.Contains("Check My permits and PayPal payment activity", message);
        Assert.DoesNotContain("nothing was charged", message);
        Assert.Equal(PaymentStatus.Paid, (await Reload(world, application.Id)).PaymentStatus);
        Assert.Equal(1, world.PayPal.CaptureCalls);
    }

    [Fact]
    public async Task OtherPersonsCancel_DoesNotDisclosePaidReceiptOrPaymentState()
    {
        var world = NewWorld();
        using var connection = world.Connection;
        using var db = world.Db;
        var application = AddApplication(world.Db, orderId: "ORDER-1", studentId: "maria");
        await world.Checkout.CaptureAsync(application.Id);
        var controller = NewCheckoutController(world, "diego");

        var result = await controller.Cancel("ORDER-1", default);

        Assert.Equal("/", Assert.IsType<RedirectResult>(result).Url);
        Assert.False(controller.TempData.ContainsKey("StatusMessage"));
        Assert.False(controller.TempData.ContainsKey("CheckoutInfo"));
        Assert.Equal(PaymentStatus.Paid, (await Reload(world, application.Id)).PaymentStatus);
        Assert.Equal(1, world.PayPal.CaptureCalls);
    }
}
