using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Builds the one-page parking permit receipt PDF for a LIVE permit
/// (REDO-PLAN items 43–45) — the document the student prints for the
/// windshield and the receipt of record. The shape is the reference
/// app's receipt structure, rebuilt fresh: title + org + rule →
/// summary box → details table → "Proof of Parking (For
/// Enforcement)" section → footer, generated in memory on EVERY
/// call (no cached bytes — a cache would be a second truth that can
/// go stale).
///
/// The controller (MyPermitsController.PermitPdf) owns the gate —
/// owner only, Paid or NotRequired only — this service just renders
/// the application it is handed.
///
/// QuestPDF runs under its Community License
/// (QuestPDF.Settings.License below) — NOT MIT/Apache; see
/// NOTICES.md for the terms and why this project qualifies.
/// </summary>
public sealed class PermitPdfService
{
    private static readonly TimeZoneInfo PacificZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    /// <summary>
    /// Renders the receipt PDF. <paramref name="publicBaseUrl"/> is
    /// the app's public origin from the live request (scheme + host,
    /// e.g. "https://localhost:7156") used to build the verify URL;
    /// null off-request (tests) falls back to localhost.
    /// </summary>
    public byte[] BuildPermitPdf(PermitApplication app, string? publicBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var generatedPacific =
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PacificZone);

        var origin = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? "https://localhost"
            : publicBaseUrl.TrimEnd('/');
        var verifyUrl = app.VerifyToken is null
            ? null
            : $"{origin}/verify?t={Uri.EscapeDataString(app.VerifyToken)}";

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

        // The summary box's payment line (item 44): the Employee
        // exemption is named as such; any other free permit says it
        // was issued free and names the campus whose published fee
        // is $0; a paid permit shows PAID + the amount.
        var isEmployee = app.Category == UserCategory.Employee;
        var paymentLine =
            app.PaymentStatus == PaymentStatus.NotRequired && isEmployee
                ? "Payment: EXEMPT (Employee)"
                : app.PaymentStatus == PaymentStatus.NotRequired
                    ? $"Payment: FREE — ISSUED ({app.Campus} charges $0)"
                    : $"Payment: PAID {Money(app.PriceCents)} USD";

        // The privilege line (item 44): staff vs the combined
        // student/public wording.
        var privilegeLine = isEmployee
            ? "Staff Parking Eligible"
            : "Student/Public Parking Eligible";

        var issuedLine = app.ReceiptIssuedAtUtc is { } issuedUtc
            ? $"Issued: {Pacific(issuedUtc)} PT"
            : "Issued: (pending)";

        var validLine = $"Valid: {Date(app.StartDate)} → {Date(app.EndDate)}";

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
                    row.RelativeItem().AlignRight().Text("PARKING PERMIT RECEIPT")
                        .FontSize(13).SemiBold().FontColor(Colors.White);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    // Title + org + rule (item 44).
                    col.Item().Text("Parking Permit Receipt").FontSize(16).SemiBold();
                    col.Item().Text(app.Campus).FontSize(13);
                    col.Item().LineHorizontal(1);

                    // Summary box: the windshield facts, big.
                    col.Item().Background(Colors.Grey.Lighten4).Padding(12).Column(box =>
                    {
                        box.Spacing(4);

                        box.Item().Text($"Permit #: {app.ReceiptNumber ?? "(pending)"}")
                            .FontSize(14).SemiBold();
                        box.Item().Text(issuedLine);
                        box.Item().Text($"PLATE:  {plate}")
                            .FontSize(22).Bold();
                        box.Item().Text(paymentLine);
                        box.Item().Text(privilegeLine);
                        box.Item().Text($"Amount: {Money(app.PriceCents)} USD");
                        box.Item().Text(validLine).FontSize(13).SemiBold();
                    });

                    // Details table (item 44): type, vehicle, dates,
                    // category, paid-at, and the PayPal ids — the
                    // processor proof (Duvara's shape; PayPal, not
                    // Stripe, is Solvia's processor).
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(150);
                            c.RelativeColumn();
                        });

                        Row(t, "College", app.Campus);
                        Row(t, "Student", studentFirstName);
                        Row(t, "Permit Type", app.PermitType?.Name ?? "Parking permit");
                        Row(t, "Vehicle", vehicleCell);
                        Row(t, "Dates", $"{Date(app.StartDate)} → {Date(app.EndDate)}");
                        Row(t, "Category", app.Category.ToString());
                        Row(t, "Paid At (UTC)",
                            app.PaidAtUtc?.ToString("u", CultureInfo.InvariantCulture)
                                .Replace("Z", " UTC") ?? "—");
                        Row(t, "PayPal Order ID", app.PayPalOrderId ?? "—");
                        Row(t, "PayPal Capture ID", app.PayPalCaptureId ?? "—");
                    });

                    if (isEmployee)
                    {
                        col.Item().Text(
                            "Note: employee permits are exempt from the " +
                            "parking fee — no payment is required or collected.");
                    }

                    col.Item().LineHorizontal(1);

                    // Enforcement section (item 44): anyone can check
                    // this permit is real at the public /verify page.
                    col.Item().Background(Colors.Blue.Lighten5).Padding(12).Column(v =>
                    {
                        v.Spacing(4);
                        v.Item().Text("Proof of Parking (For Enforcement)")
                            .FontSize(13).SemiBold();
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

    private static string Pacific(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), PacificZone)
            .ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    private static string Money(int cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) =>
        date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
