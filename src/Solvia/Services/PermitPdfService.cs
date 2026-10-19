using System.Globalization;
using Microsoft.AspNetCore.Http;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Builds the one-page printable permit PDF for a LIVE permit —
/// the document the student prints for the windshield and the
/// receipt of record. The approach mirrors the reference app's
/// receipt-PDF structure (fresh code): a single QuestPDF document,
/// header → summary box → details table → verification section →
/// footer, generated in memory and served by the controller only
/// when the application is Paid or NotRequired (see
/// PermitsController.PermitPdf, which owns that gate).
///
/// QuestPDF is used under its Community License
/// (QuestPDF.Settings.License below) — see NOTICES.md for the
/// terms and why this project qualifies.
/// </summary>
public sealed class PermitPdfService
{
    private readonly IHttpContextAccessor _http;

    public PermitPdfService(IHttpContextAccessor http)
    {
        _http = http;
    }

    public byte[] BuildPermitPdf(PermitApplication app)
    {
        // The generated stamp, shown in Pacific time like the
        // reference app's receipts.
        var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var generatedPacific = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacificZone);

        // The public check-anyone link for this permit, built from
        // the live request (the reference builds its verify URL the
        // same way); off-request (tests) it falls back to localhost.
        var request = _http.HttpContext?.Request;
        var scheme = request?.Scheme ?? "https";
        var host = request?.Host.Value ?? "localhost";
        var verifyUrl = app.VerifyToken is null
            ? null
            : $"{scheme}://{host}/verify?t={Uri.EscapeDataString(app.VerifyToken)}";

        var vehicle = app.Vehicle;
        var carParts = new[]
        {
            vehicle?.Color.ToString(),
            vehicle?.Make,
            vehicle?.Model,
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        var carLine = string.Join(" ", carParts);
        var plate = vehicle?.Plate ?? "—";
        var vehicleCell = vehicle is null
            ? "—"
            : $"{(carLine.Length > 0 ? carLine + " — " : "")}plate {plate} ({vehicle.State})";

        var studentFirstName = (app.Student?.DisplayName ?? "Student")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Student";

        var paymentLine = app.PaymentStatus == PaymentStatus.NotRequired
            ? "Payment: FREE — no payment was needed"
            : $"Payment: PAID {Money(app.PriceCents)} USD";
        var validLine =
            $"Valid: {Date(app.StartDate)} → {Date(app.EndDate)}";

        // QuestPDF license (Community — see NOTICES.md).
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter); // 8.5 x 11
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(11));

                // Navy header band: the brand + what this paper is.
                page.Header().Background(Colors.Blue.Darken3).Padding(12).Row(row =>
                {
                    row.RelativeItem().Text("SOLVIA")
                        .FontSize(20).Bold().FontColor(Colors.White);
                    row.RelativeItem().AlignRight().Text("PARKING PERMIT")
                        .FontSize(14).SemiBold().FontColor(Colors.White);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text(app.Campus).FontSize(14).SemiBold();
                    col.Item().LineHorizontal(1);

                    // Summary box: the windshield facts, big.
                    col.Item().Background(Colors.Grey.Lighten4).Padding(12).Column(box =>
                    {
                        box.Spacing(4);

                        box.Item().Text($"Permit #: {app.ReceiptNumber ?? "(pending)"}")
                            .FontSize(14).SemiBold();
                        box.Item().Text($"PLATE:  {plate}")
                            .FontSize(22).Bold();
                        box.Item().Text(paymentLine);
                        box.Item().Text($"Amount: {Money(app.PriceCents)} USD");
                        box.Item().Text(validLine).FontSize(13).SemiBold();
                    });

                    // Details table (key/value), reference order:
                    // place, person, car, permit, dates, payment ids.
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(150);
                            c.RelativeColumn();
                        });

                        Row(t, "College", app.Campus);
                        Row(t, "Student", studentFirstName);
                        Row(t, "Vehicle", vehicleCell);
                        Row(t, "Permit", app.PermitType?.Name ?? "Parking permit");
                        Row(t, "Valid from", Date(app.StartDate));
                        Row(t, "Valid through", Date(app.EndDate));
                        Row(t, "Paid at (UTC)",
                            app.PaidAtUtc?.ToString("u", CultureInfo.InvariantCulture)
                                .Replace("Z", " UTC") ?? "—");
                        Row(t, "PayPal order ID", app.PayPalOrderId ?? "—");
                        Row(t, "PayPal capture ID", app.PayPalCaptureId ?? "—");
                    });

                    col.Item().LineHorizontal(1);

                    // Verification section: anyone can check this
                    // permit is real at the public /verify page.
                    col.Item().Background(Colors.Blue.Lighten5).Padding(12).Column(v =>
                    {
                        v.Spacing(4);
                        v.Item().Text("Check this permit").FontSize(13).SemiBold();
                        if (verifyUrl is not null)
                        {
                            v.Item().Text("Anyone can check this permit is real:");
                            v.Item().Text(verifyUrl)
                                .FontColor(Colors.Grey.Darken2).FontSize(9);
                            v.Item().Border(1).BorderColor(Colors.Grey.Darken1)
                                .Padding(6).Text(app.VerifyToken!).SemiBold();
                        }
                        else
                        {
                            v.Item().Text("Verification token not issued yet.");
                        }
                    });
                });

                page.Footer().AlignCenter().Column(f =>
                {
                    f.Item().AlignCenter().Text(
                        "Demo permit — fictional student, PayPal Sandbox.");
                    f.Item().AlignCenter().Text(txt =>
                    {
                        txt.Span("Generated by Solvia • ");
                        txt.Span($"{generatedPacific:g} PT");
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Row(TableDescriptor t, string label, string value)
    {
        t.Cell().PaddingVertical(3).Text(label).SemiBold();
        t.Cell().PaddingVertical(3).Text(value);
    }

    private static string Money(int cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) =>
        date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
