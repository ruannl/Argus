using System.Text.Json;
using System.Text.Json.Serialization;

namespace argus.Server.Imports;

public sealed record PdfChildRequestOptions(DateOnly? StartDate,
                                            DateOnly? EndDate,
                                            int? Century,
                                            int DeadlineMilliseconds);

public sealed record PdfChildRow(Guid RowId,
                                 int SourceLine,
                                 int? SourcePage,
                                 int SourceRowNumber,
                                 Dictionary<string, string?> RawValues,
                                 string? TransactionDate,
                                 string Description,
                                 string NormalizedDescription,
                                 long? AmountCents,
                                 string[] ValidationCodes,
                                 string[] Warnings,
                                 string? SourceIdNamespace,
                                 string? SourceIdValue);

public sealed record PdfChildStatement(string Format,
                                       PdfChildRow[] Rows,
                                       string? ProposedStartDate,
                                       string? ProposedEndDate,
                                       Dictionary<string, string> MappingSuggestions,
                                       string[] RequiredConfirmations,
                                       string[] Warnings);

public sealed record PdfChildResponse(
    bool Ok,
    string? Reason,
    PdfChildStatement? Statement);

public static class PdfChildProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PdfChildStatement ToChild(ParsedStatement statement) => new(
        StatementText.SourceType(statement.Format),
        statement.Rows.Select(row => new PdfChildRow(
            row.RowId,
            row.SourceLine,
            row.SourcePage,
            row.SourceRowNumber,
            new Dictionary<string, string?>(row.RawValues, StringComparer.Ordinal),
            row.TransactionDate?.ToString("yyyy-MM-dd"),
            row.Description,
            row.NormalizedDescription,
            row.AmountCents,
            row.ValidationCodes.ToArray(),
            row.Warnings.ToArray(),
            row.SourceIdNamespace,
            row.SourceIdValue)).ToArray(),
        statement.ProposedStartDate?.ToString("yyyy-MM-dd"),
        statement.ProposedEndDate?.ToString("yyyy-MM-dd"),
        new Dictionary<string, string>(statement.MappingSuggestions, StringComparer.Ordinal),
        statement.RequiredConfirmations.ToArray(),
        statement.Warnings.ToArray());

    public static ParsedStatement FromChild(PdfChildStatement statement)
    {
        var format = statement.Format switch
        {
            "standard_bank_pdf" => StatementFormat.StandardBankPdf,
            _ => throw new StatementRejectedException(ParserReasons.UnsupportedLayout),
        };

        return new ParsedStatement(
            format,
            statement.Rows.Select(row => new ParsedStatementRow(
                row.RowId,
                row.SourceLine,
                row.SourcePage,
                row.SourceRowNumber,
                new Dictionary<string, string?>(row.RawValues, StringComparer.Ordinal),
                ParseDate(row.TransactionDate),
                row.Description,
                row.NormalizedDescription,
                row.AmountCents,
                row.ValidationCodes,
                row.Warnings,
                row.SourceIdNamespace,
                row.SourceIdValue)).ToArray(),
            ParseDate(statement.ProposedStartDate),
            ParseDate(statement.ProposedEndDate),
            new Dictionary<string, string>(statement.MappingSuggestions, StringComparer.Ordinal),
            statement.RequiredConfirmations,
            statement.Warnings);
    }

    private static DateOnly? ParseDate(string? value) =>
        value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) ? date : null;
}
