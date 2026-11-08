using System.Globalization;
using System.Text;

namespace argus.Server.Imports;

public static class StatementText
{
    public static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (builder.Length > 0)
                {
                    pendingSpace = true;
                }
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    public static int CountScalars(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Display keeps meaningful text; matching collapses whitespace and uses
    /// invariant case folding. An empty description has an empty matching value
    /// and a display fallback that is never used as identity. CSV preserves its
    /// source/display whitespace (including quoted newlines and internal tabs)
    /// while still trimming the documented outer padding; PDF continuation
    /// fragments continue to collapse their display whitespace.
    /// </summary>
    public static (string Display, string Normalized, bool Empty, bool TooLong) NormalizeDescription(string? raw,
                                                                                                     bool collapseDisplay = true)
    {
        var source = (raw ?? string.Empty).Trim();
        var empty = source.Length == 0;
        var display = empty
            ? "No description"
            : collapseDisplay ? CollapseWhitespace(source) : source;
        var tooLong = CountScalars(display) > ImportLimits.MaxDescriptionScalars;
        if (tooLong)
        {
            display = TruncateScalars(display, ImportLimits.MaxDescriptionScalars);
        }

        // Emptiness comes from the source, never from the display fallback. A
        // literal "No description" is a real description with its own identity.
        var normalized = empty ? string.Empty : CollapseWhitespace(display).ToLowerInvariant();
        return (display, normalized, empty, tooLong);
    }

    public static string TruncateScalars(string value,
                                         int max)
    {
        if (CountScalars(value) <= max)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ == max)
            {
                break;
            }
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    public static string FormatCents(long cents)
    {
        var value = cents / 100m;
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static string SourceType(StatementFormat format) => format switch
    {
        StatementFormat.FnbCsv => "fnb_csv",
        StatementFormat.FnbZip => "fnb_zip",
        StatementFormat.StandardBankPdf => "standard_bank_pdf",
        _ => "generic_csv",
    };
}

public static class StatementDates
{
    private static readonly string[] _fullYearFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "d/M/yyyy",
        "M/d/yyyy",
        "dd MMM yyyy",
        "d MMM yyyy",
    ];

    private static readonly string[] _shortYearFormats =
    [
        "dd/MM/yy",
        "MM/dd/yy",
        "d/M/yy",
        "M/d/yy",
        "dd MMM yy",
        "d MMM yy",
        "yy-MM-dd",
        "yy/MM/dd",
    ];

    public static bool TryParseFullYear(string value,
                                        out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), _fullYearFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    public static bool TryParseFullYearWithFormat(string value,
                                                  string format,
                                                  out DateOnly date)
    {
        date = default;
        if (!DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return false;
        }
        date = DateOnly.FromDateTime(parsed);
        return true;
    }

    public static bool TryParseTwoDigitYearWithFormat(string value,
                                                      string format,
                                                      out int day,
                                                      out int month,
                                                      out int year2)
    {
        day = 0;
        month = 0;
        year2 = 0;
        if (!DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return false;
        }
        day = parsed.Day;
        month = parsed.Month;
        year2 = parsed.Year % 100;
        return true;
    }

    public static bool TryParseTwoDigitYear(string value,
                                            out int day,
                                            out int month,
                                            out int year2)
    {
        day = 0;
        month = 0;
        year2 = 0;
        var trimmed = value.Trim();
        foreach (var format in _shortYearFormats)
        {
            if (!DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                continue;
            }

            if (format.Contains("MMM", StringComparison.Ordinal))
            {
                day = parsed.Day;
                month = parsed.Month;
                year2 = parsed.Year % 100;
                return true;
            }

            if (format.StartsWith("yy", StringComparison.Ordinal))
            {
                day = parsed.Day;
                month = parsed.Month;
                year2 = parsed.Year % 100;
                return true;
            }

            day = parsed.Day;
            month = parsed.Month;
            year2 = parsed.Year % 100;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves a two-digit year only when exactly one century fits the confirmed
    /// statement period, or when the customer supplied an explicit century.
    /// </summary>
    public static (DateOnly? Date, bool CenturyRequired) Resolve(int day,
                                                                 int month,
                                                                 int year2,
                                                                 DateOnly? start,
                                                                 DateOnly? end,
                                                                 int? century)
    {
        if (start is null || end is null)
        {
            return (null, false);
        }

        if (century is not null)
        {
            var year = century.Value + year2;
            return TryCreate(year, month, day) is { } date && InPeriod(date, start.Value, end.Value)
                ? (date, false)
                : (null, false);
        }

        var matches = new List<DateOnly>();
        for (var year = 1900; year <= 2099; year++)
        {
            if (year % 100 != year2)
            {
                continue;
            }
            if (TryCreate(year, month, day) is { } date && InPeriod(date, start.Value, end.Value))
            {
                matches.Add(date);
            }
        }

        if (matches.Count == 1)
        {
            return (matches[0], false);
        }
        if (matches.Count > 1)
        {
            return (null, true);
        }
        return (null, false);
    }

    public static bool InPeriod(DateOnly date,
                                DateOnly start,
                                DateOnly end) =>
        date >= start && date <= end;

    public static bool IsFuture(DateOnly date) => date > DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly? TryCreate(int year,
                                       int month,
                                       int day)
    {
        if (year is < 1900 or > 2099 || month is < 1 or > 12 || day < 1)
        {
            return null;
        }
        if (day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }
        return new DateOnly(year, month, day);
    }
}

public static class StatementAmounts
{
    public const long MaxAbsoluteCents = 999_999_999_999_999_999L;

    public static bool TryParseSignedDecimal(string? raw,
                                             out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim();
        var negative = false;
        if (value.StartsWith('+'))
        {
            value = value[1..];
        }
        else if (value.StartsWith('-'))
        {
            negative = true;
            value = value[1..];
        }

        if (!TryParseDecimalDigits(value, '.', out var magnitude))
        {
            return false;
        }

        cents = negative ? -magnitude : magnitude;
        if (cents == 0)
        {
            cents = 0;
        }
        return true;
    }

    public static bool TryParseMagnitude(string? raw,
                                         string decimalSeparator,
                                         string thousandsSeparator,
                                         out long cents,
                                         out bool hasPositiveSign,
                                         out bool hasNegativeSign)
    {
        cents = 0;
        hasPositiveSign = false;
        hasNegativeSign = false;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim();
        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            hasNegativeSign = true;
            value = value[1..^1].Trim();
        }

        if (value.EndsWith("CR", StringComparison.OrdinalIgnoreCase))
        {
            hasPositiveSign = true;
            value = value[..^2].TrimEnd();
        }
        else if (value.EndsWith("DR", StringComparison.OrdinalIgnoreCase))
        {
            hasNegativeSign = true;
            value = value[..^2].TrimEnd();
        }

        if (value.StartsWith('+'))
        {
            hasPositiveSign = true;
            value = value[1..].TrimStart();
        }
        else if (value.StartsWith('-'))
        {
            hasNegativeSign = true;
            value = value[1..].TrimStart();
        }

        if (value.EndsWith('-'))
        {
            hasNegativeSign = true;
            value = value[..^1].TrimEnd();
        }
        else if (value.EndsWith('+'))
        {
            hasPositiveSign = true;
            value = value[..^1].TrimEnd();
        }

        if (hasPositiveSign && hasNegativeSign)
        {
            return false;
        }

        if (decimalSeparator == thousandsSeparator)
        {
            return false;
        }

        var normalized = NormalizeGrouping(value, decimalSeparator, thousandsSeparator);
        if (normalized is null)
        {
            return false;
        }

        if (!TryParseDecimalDigits(normalized, '.', out var magnitude))
        {
            return false;
        }

        cents = magnitude;
        return true;
    }

    private static string? NormalizeGrouping(string value,
                                             string decimalSeparator,
                                             string thousandsSeparator)
    {
        var decimalIndex = decimalSeparator.Length == 0
            ? -1
            : value.LastIndexOf(decimalSeparator, StringComparison.Ordinal);
        var integerPart = decimalIndex < 0 ? value : value[..decimalIndex];
        var fractionPart = decimalIndex < 0 ? string.Empty : value[(decimalIndex + decimalSeparator.Length)..];

        if ((thousandsSeparator.Length > 0 &&
             fractionPart.Contains(thousandsSeparator, StringComparison.Ordinal)) ||
            (decimalSeparator.Length > 0 &&
             fractionPart.Contains(decimalSeparator, StringComparison.Ordinal)))
        {
            return null;
        }

        if (fractionPart.Length > 2)
        {
            return null;
        }

        if (thousandsSeparator.Length > 0)
        {
            if (!ValidateGrouping(integerPart, thousandsSeparator))
            {
                return null;
            }
            integerPart = integerPart.Replace(thousandsSeparator, string.Empty, StringComparison.Ordinal);
        }
        else if (integerPart.Contains(',') ||
                 integerPart.Contains('.') ||
                 integerPart.Contains(' '))
        {
            return null;
        }

        if (integerPart.Length == 0 || !integerPart.All(char.IsAsciiDigit))
        {
            return null;
        }

        return fractionPart.Length == 0 ? integerPart : $"{integerPart}.{fractionPart}";
    }

    private static bool ValidateGrouping(string integerPart,
                                         string separator)
    {
        if (integerPart.Length == 0)
        {
            return false;
        }

        var groups = integerPart.Split(separator, StringSplitOptions.None);
        if (groups.Length == 1)
        {
            return groups[0].Length > 0 && groups[0].All(char.IsAsciiDigit);
        }

        if (groups[0].Length is < 1 or > 3 || !groups[0].All(char.IsAsciiDigit))
        {
            return false;
        }

        for (var index = 1; index < groups.Length; index++)
        {
            if (groups[index].Length != 3 || !groups[index].All(char.IsAsciiDigit))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParseDecimalDigits(string value,
                                              char separator,
                                              out long cents)
    {
        cents = 0;
        var parts = value.Split(separator);
        if (parts.Length > 2)
        {
            return false;
        }

        var integerPart = parts[0];
        var fractionPart = parts.Length == 2 ? parts[1] : string.Empty;
        if (integerPart.Length == 0 || !integerPart.All(char.IsAsciiDigit))
        {
            return false;
        }
        if (fractionPart.Length > 2 || !fractionPart.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (integerPart.Length > 16)
        {
            return false;
        }

        if (!long.TryParse(integerPart, NumberStyles.None, CultureInfo.InvariantCulture,
                out var whole))
        {
            return false;
        }

        var fraction = fractionPart.Length switch
        {
            0 => 0,
            1 => (fractionPart[0] - '0') * 10,
            _ => (fractionPart[0] - '0') * 10 + (fractionPart[1] - '0'),
        };

        if (whole > MaxAbsoluteCents / 100)
        {
            return false;
        }

        cents = checked(whole * 100 + fraction);
        return Math.Abs(cents) <= MaxAbsoluteCents;
    }
}
