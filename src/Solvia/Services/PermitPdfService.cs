using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>Printable Letter-size permit. PDFsharp is MIT; embedded Roboto is Apache-2.0.</summary>
public sealed class PermitPdfService
{
    private static readonly TimeZoneInfo PacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly XColor Navy = XColor.FromArgb(7, 27, 70);
    private static readonly XColor Teal = XColor.FromArgb(0, 127, 121);
    private static readonly XColor Muted = XColor.FromArgb(76, 94, 112);
    private static readonly XColor Line = XColor.FromArgb(220, 228, 236);

    static PermitPdfService() => GlobalFontSettings.FontResolver ??= new PermitFontResolver();

    public byte[] BuildPermitPdf(PermitApplication app, string? publicBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.NotRequired))
            throw new InvalidOperationException("A printable permit requires payment or an approved exemption.");

        using var document = new PdfDocument();
        document.Info.Title = $"Solvia permit {app.ReceiptNumber}";
        document.Info.Author = "Solvia";
        document.Info.Subject = "Demo parking permit - fictional person and sandbox payment";
        var page = document.AddPage();
        page.Size = PageSize.Letter;
        using var graphics = XGraphics.FromPdfPage(page);
        var ink = new XSolidBrush(Navy);
        var muted = new XSolidBrush(Muted);
        var regular = new XFont("Solvia Sans", 10, XFontStyleEx.Regular);
        var bold = new XFont("Solvia Sans", 10, XFontStyleEx.Bold);
        var small = new XFont("Solvia Sans", 8.5, XFontStyleEx.Regular);

        void Text(string value, double x, double y, XFont font, XBrush brush) =>
            graphics.DrawString(value, font, brush, new XPoint(x, y), XStringFormats.TopLeft);
        double Wrapped(string value, double x, double y, double width, XFont font, XBrush brush, double lineHeight)
        {
            foreach (var line in Wrap(graphics, value, font, width))
            {
                Text(line, x, y, font, brush);
                y += lineHeight;
            }
            return y;
        }

        graphics.DrawRectangle(new XSolidBrush(Navy), 0, 0, 612, 88);
        Text("Solvia", 40, 22, new XFont("Solvia Sans", 28, XFontStyleEx.Bold), XBrushes.White);
        Text("PARKING PERMIT + RECEIPT", 40, 59, new XFont("Solvia Sans", 9, XFontStyleEx.Bold), XBrushes.White);
        Text("SANDBOX DEMO", 457, 31, bold, XBrushes.White);
        Wrapped(app.Campus, 40, 106, 532, new XFont("Solvia Sans", 14, XFontStyleEx.Bold), ink, 17);

        graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(242, 247, 250)), 40, 164, 532, 188);
        graphics.DrawRectangle(new XSolidBrush(Teal), 40, 164, 5, 188);
        Text("PERMIT NUMBER", 57, 178, small, muted);
        Text(app.ReceiptNumber ?? "(pending)", 57, 194, new XFont("Solvia Sans", 17, XFontStyleEx.Bold), ink);
        Text("LICENSE PLATE", 57, 227, small, muted);
        var plate = app.Vehicle?.Plate ?? "(not provided)";
        var plateSize = 30d;
        while (plateSize > 12 && graphics.MeasureString(plate, new XFont("Solvia Sans", plateSize, XFontStyleEx.Bold)).Width > 370)
            plateSize--;
        Text(plate, 57, 242, new XFont("Solvia Sans", plateSize, XFontStyleEx.Bold), ink);
        Text($"STATE: {app.Vehicle?.State}", 447, 249, bold, ink);
        Text($"Valid: {Date(app.StartDate)} to {Date(app.EndDate)}", 57, 287, new XFont("Solvia Sans", 12, XFontStyleEx.Bold), ink);
        var employee = app.Category == UserCategory.Employee;
        var payment = app.PaymentStatus == PaymentStatus.Paid ? $"PAID {Money(app.PriceCents)} USD"
            : employee ? "EXEMPT (Employee)" : "FREE - ISSUED";
        Text($"Payment: {payment}", 57, 313, bold, new XSolidBrush(Teal));
        Text(employee ? "Staff Parking Eligible" : "Student/Public Parking Eligible", 57, 332, small, ink);

        var holder = (app.Student?.DisplayName ?? (employee ? "Employee" : "Student"))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Permit holder";
        var vehicle = string.Join(" ", new[] { app.Vehicle?.Color.ToString(), app.Vehicle?.Make, app.Vehicle?.Model }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var details = new (string Label, string Value)[]
        {
            ("Permit holder", holder),
            ("Permit type", app.PermitType?.Name ?? "Parking permit"),
            ("Vehicle", $"{vehicle} ({app.Vehicle?.State})"),
            ("Category", app.Category.ToString()),
            ("Issued", app.ReceiptIssuedAtUtc is { } issued ? Pacific(issued) + " PT" : "(pending)"),
            ("Paid at (UTC)", app.PaidAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "Not required"),
            ("PayPal order ID", app.PayPalOrderId ?? "Not required"),
            ("PayPal capture ID", app.PayPalCaptureId ?? "Not required")
        };
        double y = 370;
        foreach (var (label, value) in details)
        {
            Text(label, 40, y, small, muted);
            var bottom = Wrapped(value, 178, y, 394, regular, ink, 13);
            y = Math.Max(y + 24, bottom + 9);
            graphics.DrawLine(new XPen(Line, .5), 40, y - 5, 572, y - 5);
        }

        y += 13;
        Text("Proof of Parking (For Enforcement)", 40, y, new XFont("Solvia Sans", 12, XFontStyleEx.Bold), ink);
        y += 23;
        var origin = string.IsNullOrWhiteSpace(publicBaseUrl) ? "https://localhost" : publicBaseUrl.TrimEnd('/');
        if (app.VerifyToken is { } token)
        {
            Text("Check this demo permit's current status:", 40, y, regular, ink);
            y = Wrapped($"{origin}/verify?t={Uri.EscapeDataString(token)}", 40, y + 17, 532, small, muted, 12);
        }
        else Text("Verification token not issued yet.", 40, y, regular, ink);

        if (employee)
            Text("Demo employee exemption; actual campus parking rules may differ.", 40, y + 10, small, muted);
        graphics.DrawLine(new XPen(Line, .75), 40, 734, 572, 734);
        Text("DEMO PERMIT - fictional person, PayPal Sandbox. Not a real campus authorization.", 40, 744, small, muted);
        Text($"Generated by Solvia - {Pacific(DateTime.UtcNow)} PT | Print: US Letter, actual size", 40, 760, small, muted);
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static IEnumerable<string> Wrap(XGraphics graphics, string text, XFont font, double width)
    {
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (graphics.MeasureString(candidate, font).Width <= width) { current = candidate; continue; }
            if (current.Length > 0) { yield return current; current = ""; }
            foreach (var character in word)
            {
                if (graphics.MeasureString(current + character, font).Width > width)
                { yield return current; current = ""; }
                current += character;
            }
        }
        if (current.Length > 0) yield return current;
    }

    private static string Money(int cents) => "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    private static string Pacific(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(utc, DateTimeKind.Utc), PacificZone).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    private sealed class PermitFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "Roboto-Bold" : "Roboto-Regular");
        public byte[] GetFont(string faceName)
        {
            using var stream = typeof(PermitPdfService).Assembly.GetManifestResourceStream($"Solvia.Assets.Fonts.{faceName}.ttf")
                ?? throw new InvalidOperationException("The embedded permit font is missing.");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
    }
}
