namespace argus.Server.Imports;

/// <summary>
/// Format dispatch for inspect and prepare. Inspection is always stateless;
/// preparation persists nothing and returns the same immutable parser rows.
/// </summary>
public static class StatementParser
{
    private static Task<ParsedStatement> RunCsvAsync(ParserAdmission admission, Func<CancellationToken, ParsedStatement> parse, CancellationToken cancellationToken) =>
        admission.RunAsync(
            token => Task.Run(() => parse(token), token),
            cancellationToken);

    public static async Task<ParsedStatement> InspectAsync(byte[] bytes,
                                                           StatementSourceKind sourceKind,
                                                           ParserConfig? config,
                                                           DateOnly? start,
                                                           DateOnly? end,
                                                           PdfParserRunner pdfRunner,
                                                           ParserAdmission admission,
                                                           CancellationToken cancellationToken = default)
    {
        if (bytes.Length > ImportLimits.MaxFileBytes)
        {
            throw new StatementRejectedException(ParserReasons.FileTooLarge);
        }

        return sourceKind switch
        {
            StatementSourceKind.Fnb => await RunCsvAsync(
                admission,
                token => CsvStatementParsers.ParseFnb(bytes, start, end, token),
                cancellationToken),
            StatementSourceKind.StandardBankPdf => await pdfRunner.ParseAsync(
                bytes,
                new PdfChildRequestOptions(start, end, config?.Century, ImportLimits.ParserDeadlineSeconds * 1000),
                cancellationToken),
            StatementSourceKind.GenericCsv when config is null => await RunCsvAsync(
                admission,
                token => CsvStatementParsers.InspectGeneric(
                    CsvStatementParsers.DecodeUtf8(bytes, token), token),
                cancellationToken),
            StatementSourceKind.GenericCsv => await RunCsvAsync(
                admission,
                token => CsvStatementParsers.ParseGeneric(bytes, config, start, end, token),
                cancellationToken),
            _ => throw new StatementRejectedException(ParserReasons.UnsupportedFormat),
        };
    }

    public static async Task<ParsedStatement> PrepareAsync(byte[] bytes,
                                                           StatementSourceKind sourceKind,
                                                           ParserConfig? config,
                                                           DateOnly start,
                                                           DateOnly end,
                                                           PdfParserRunner pdfRunner,
                                                           ParserAdmission admission,
                                                           CancellationToken cancellationToken = default)
    {
        if (bytes.Length > ImportLimits.MaxFileBytes)
        {
            throw new StatementRejectedException(ParserReasons.FileTooLarge);
        }

        return sourceKind switch
        {
            StatementSourceKind.Fnb => await RunCsvAsync(
                admission,
                token => CsvStatementParsers.ParseFnb(bytes, start, end, token),
                cancellationToken),
            StatementSourceKind.StandardBankPdf => await pdfRunner.ParseAsync(
                bytes,
                new PdfChildRequestOptions(start, end, config?.Century, ImportLimits.ParserDeadlineSeconds * 1000),
                cancellationToken),
            StatementSourceKind.GenericCsv when config is null || !config.IsGeneric =>
                throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired),
            StatementSourceKind.GenericCsv => await RunCsvAsync(
                admission,
                token => CsvStatementParsers.ParseGeneric(bytes, config, start, end, token),
                cancellationToken),
            _ => throw new StatementRejectedException(ParserReasons.UnsupportedFormat),
        };
    }
}
