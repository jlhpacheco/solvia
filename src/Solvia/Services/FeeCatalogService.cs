using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Loads data/fees.csv into memory and answers <see cref="IFeeCatalog"/>.
///
/// The CSV is frozen research data with free-text cells ("$229.50 per
/// semester…", "$195 Fall; $195 Spring…", "not listed on PMB page"),
/// so the loader parses each row tolerantly:
/// - "not listed…" in a fee cell ⇒ that price is null.
/// - "FREE…" in the semester-fee cell ⇒ price 0 with IsFree = true.
/// - Otherwise the first dollar amount labelled Fall / Spring in the
///   semester cell wins for that term; an unlabelled "per semester"
///   amount applies to both semesters; the "Summer"-labelled amount
///   in the other-term-fees cell is the summer price.
/// - The day-pass cell yields <see cref="CampusFee.DayPassCents"/>:
///   the student-facing segment's day rate (see ParseDayPassCents).
/// A price is NEVER defaulted or invented: anything unparseable stays
/// null, and the app will say "price not published by this campus."
/// </summary>
public sealed class FeeCatalogService : IFeeCatalog
{
    private static readonly Regex MoneyRegex = new(
        @"\$(\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)",
        RegexOptions.Compiled);

    private readonly IReadOnlyList<CampusFee> _campuses;
    private readonly Dictionary<string, CampusFee> _byName;

    /// <summary>Loads and parses the whole CSV eagerly; a missing file fails fast.</summary>
    public FeeCatalogService(string csvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException(
                $"College fee data file not found: '{csvPath}'. " +
                "Set FEES_CSV_PATH (or FeesCsvPath) to the repo's data/fees.csv.",
                csvPath);
        }

        _campuses = Load(csvPath);
        _byName = new Dictionary<string, CampusFee>(StringComparer.OrdinalIgnoreCase);
        foreach (var campus in _campuses)
        {
            _byName[Normalize(campus.Campus)] = campus;
        }
    }

    public CampusFee? Find(string campusName)
    {
        if (string.IsNullOrWhiteSpace(campusName))
        {
            return null;
        }

        return _byName.TryGetValue(Normalize(campusName), out var fee) ? fee : null;
    }

    public IReadOnlyList<CampusFee> All() => _campuses;

    public int? SemesterPriceCents(CampusFee fee, TermKind term)
    {
        ArgumentNullException.ThrowIfNull(fee);
        return term switch
        {
            TermKind.Fall => fee.FallSemesterCents ?? fee.SpringSemesterCents,
            TermKind.Spring => fee.SpringSemesterCents ?? fee.FallSemesterCents,
            TermKind.Summer => fee.SummerSemesterCents,
            _ => null,
        };
    }

    private static string Normalize(string name) => name.Trim();

    // ------------------------------------------------------------------
    // Loading + parsing
    // ------------------------------------------------------------------

    private static IReadOnlyList<CampusFee> Load(string csvPath)
    {
        var rows = ReadCsv(File.ReadAllText(csvPath));
        if (rows.Count < 2)
        {
            throw new InvalidDataException(
                $"Fee CSV '{csvPath}' has a header but no campus rows.");
        }

        var header = rows[0];
        int Col(string name) => header.FindIndex(
            h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
        int campusCol = Col("Campus");
        int semesterCol = Col("Student semester fee");
        int otherCol = Col("Other term fees");
        int dayCol = Col("Day pass");
        int motoCol = Col("Motorcycle");
        int sourceCol = Col("Source note");
        if (campusCol < 0 || semesterCol < 0)
        {
            throw new InvalidDataException(
                $"Fee CSV '{csvPath}' is missing required columns " +
                "('Campus', 'Student semester fee').");
        }

        string Cell(IReadOnlyList<string> row, int col) =>
            col >= 0 && col < row.Count ? row[col].Trim() : "";

        var campuses = new List<CampusFee>();
        foreach (var row in rows.Skip(1))
        {
            var campus = Cell(row, campusCol);
            if (campus.Length == 0)
            {
                continue;
            }

            var semesterText = Cell(row, semesterCol);
            var otherText = Cell(row, otherCol);
            var (fall, spring, isFree) = ParseSemesterFees(semesterText);
            var summer = ParseSummerFee(otherText);
            var dayPassText = Cell(row, dayCol);

            campuses.Add(new CampusFee(
                Campus: campus,
                StudentSemesterFeeText: semesterText,
                OtherTermFeesText: otherText,
                DayPassText: dayPassText,
                MotorcycleText: Cell(row, motoCol),
                FallSemesterCents: fall,
                SpringSemesterCents: spring,
                SummerSemesterCents: summer,
                IsFree: isFree,
                SourceNote: Cell(row, sourceCol))
            {
                DayPassCents = ParseDayPassCents(dayPassText),
            });
        }

        return campuses;
    }

    /// <summary>
    /// Parses the "Student semester fee" cell into (Fall, Spring, IsFree).
    /// Unlabelled per-semester amounts apply to both semesters.
    /// </summary>
    private static (int? Fall, int? Spring, bool IsFree) ParseSemesterFees(string cell)
    {
        if (cell.Length == 0 ||
            cell.StartsWith("not listed", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, false);
        }

        if (cell.StartsWith("FREE", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0, true);
        }

        int? fall = null;
        int? spring = null;
        foreach (var segment in cell.Split(';'))
        {
            var cents = FirstMoneyCents(segment);
            if (cents is null)
            {
                continue;
            }

            var hasFall = segment.Contains("Fall", StringComparison.OrdinalIgnoreCase);
            var hasSpring = segment.Contains("Spring", StringComparison.OrdinalIgnoreCase);
            if (hasFall)
            {
                fall ??= cents;
            }

            if (hasSpring)
            {
                spring ??= cents;
            }

            if (!hasFall && !hasSpring)
            {
                // e.g. "$229.50 per semester" — the plain per-semester rate.
                fall ??= cents;
                spring ??= cents;
            }
        }

        return (fall, spring, false);
    }

    /// <summary>Parses the "Summer"-labelled amount out of the other-term-fees cell.</summary>
    private static int? ParseSummerFee(string otherTermFeesCell)
    {
        if (otherTermFeesCell.Length == 0 ||
            otherTermFeesCell.StartsWith("not listed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var segment in otherTermFeesCell.Split(';'))
        {
            if (segment.Contains("Summer", StringComparison.OrdinalIgnoreCase))
            {
                return FirstMoneyCents(segment);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the "Day pass" cell into a per-day price in cents.
    ///
    /// The cells mix shapes: "$6/day (…)", a bare "$4.00", "$1.00
    /// (online)", "$5 Student/Other; $2 Staff…", and the trap
    /// "$2/hour or $10/day (dispensers)" — an hourly rate sitting in
    /// front of the day rate. Rule: read the FIRST segment (later
    /// segments are staff/other-audience rates); inside it, an amount
    /// explicitly labelled as the day rate ("/day", "per day") beats
    /// an earlier unlabelled or hourly amount; otherwise the first
    /// amount wins. "not listed" / no amount ⇒ null.
    /// </summary>
    private static int? ParseDayPassCents(string cell)
    {
        if (cell.Length == 0 ||
            cell.StartsWith("not listed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segment = cell.Split(';')[0];
        int? first = null;
        foreach (Match match in MoneyRegex.Matches(segment))
        {
            var cents = ToCents(match);
            first ??= cents;

            // What immediately follows this amount decides whether it
            // is the day rate ("/day", "per day", "a day").
            var after = segment.AsSpan(match.Index + match.Length)
                .TrimStart();
            if (after.StartsWith("/day", StringComparison.OrdinalIgnoreCase) ||
                after.StartsWith("per day", StringComparison.OrdinalIgnoreCase) ||
                after.StartsWith("a day", StringComparison.OrdinalIgnoreCase))
            {
                return cents;
            }
        }

        return first;
    }

    private static int? FirstMoneyCents(string text)
    {
        var match = MoneyRegex.Match(text);
        return match.Success ? ToCents(match) : null;
    }

    private static int ToCents(Match match)
    {
        var value = decimal.Parse(
            match.Groups[1].Value.Replace(",", ""),
            NumberStyles.Number,
            CultureInfo.InvariantCulture);
        return (int)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Minimal RFC-4180-style CSV reader: quoted fields, embedded
    /// commas and newlines, and "" as an escaped quote — everything
    /// the frozen research file uses.
    /// </summary>
    private static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // Drop a trailing all-empty row left by a final newline.
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrWhiteSpace))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }
}
