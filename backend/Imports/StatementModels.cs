namespace argus.Server.Imports;

public enum StatementSourceKind
{
    Fnb,
    StandardBankPdf,
    GenericCsv,
}

public enum StatementFormat
{
    FnbCsv,
    FnbZip,
    StandardBankPdf,
    GenericCsv,
}

public static class StatementSourceKinds
{
    public static bool TryParse(string? value,
                                out StatementSourceKind kind)
    {
        switch (value)
        {
            case "fnb":
            case "fnb_csv":
            case "fnb_zip":
                kind = StatementSourceKind.Fnb;
                return true;
            case "standard_bank_pdf":
                kind = StatementSourceKind.StandardBankPdf;
                return true;
            case "generic_csv":
                kind = StatementSourceKind.GenericCsv;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

public static class ValidationCodes
{
    public const string InvalidDate = "invalid_date";
    public const string DateOutsidePeriod = "date_outside_period";
    public const string FutureDate = "future_date";
    public const string CenturyRequired = "century_required";
    public const string InvalidAmount = "invalid_amount";
    public const string MissingAmount = "missing_amount";
    public const string MultipleAmounts = "multiple_amounts";
    public const string InvalidFieldCount = "invalid_field_count";
    public const string EmptyDescription = "empty_description";
    public const string DescriptionTooLong = "description_too_long";
    public const string InvalidSign = "invalid_sign";
    public const string IdentityConflict = "identity_conflict";
}

public static class ParserReasons
{
    public const string InvalidRequest = "invalid_request";
    public const string FileTooLarge = "file_too_large";
    public const string UnsupportedFormat = "unsupported_format";
    public const string UnsupportedEncoding = "unsupported_encoding";
    public const string UnsafeArchive = "unsafe_archive";
    public const string EncryptedPdf = "encrypted_pdf";
    public const string TextPdfRequired = "text_pdf_required";
    public const string UnsupportedLayout = "unsupported_layout";
    public const string ProcessingLimit = "processing_limit";
    public const string FormatConfirmationRequired = "format_confirmation_required";
    public const string ImportBusy = "import_busy";
    public const string PdfImportDeferred = "pdf_import_deferred";
}

/// <summary>
/// A whole-file parser rejection. The reason is one of the frozen safe codes and
/// never contains source text, a filename, a path, or an exception message.
/// </summary>
public sealed class StatementRejectedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Confirmed or proposed generic mapping. FNB and Standard Bank presets ignore
/// every field except <see cref="Century"/> when a two-digit year needs it.
/// </summary>
public sealed record ParserConfig
{
    public string? Format { get; init; }
    public int? HeaderLine { get; init; }
    public string? Delimiter { get; init; }
    public string? DateFormat { get; init; }
    public string? DecimalSeparator { get; init; }
    public string? ThousandsSeparator { get; init; }
    public string? AmountMode { get; init; }
    public int? DateColumn { get; init; }
    public int? DescriptionColumn { get; init; }
    public int? AmountColumn { get; init; }
    public int? DebitColumn { get; init; }
    public int? CreditColumn { get; init; }
    public string? Direction { get; init; }
    public string? NegativeStyle { get; init; }
    public bool ConfirmEmptyDescription { get; init; }
    public int? StableIdColumn { get; init; }
    public string? SourceNamespace { get; init; }
    public int? Century { get; init; }

    public bool IsGeneric => string.Equals(Format, "generic", StringComparison.Ordinal);
}

public sealed record ParsedStatementRow(Guid RowId,
                                        int SourceLine,
                                        int? SourcePage,
                                        int SourceRowNumber,
                                        IReadOnlyDictionary<string, string?> RawValues,
                                        DateOnly? TransactionDate,
                                        string Description,
                                        string NormalizedDescription,
                                        long? AmountCents,
                                        IReadOnlyList<string> ValidationCodes,
                                        IReadOnlyList<string> Warnings,
                                        string? SourceIdNamespace,
                                        string? SourceIdValue)
{
    public bool IsValid => ValidationCodes.Count == 0;
}

public sealed record ParsedStatement(StatementFormat Format,
                                     IReadOnlyList<ParsedStatementRow> Rows,
                                     DateOnly? ProposedStartDate,
                                     DateOnly? ProposedEndDate,
                                     IReadOnlyDictionary<string, string> MappingSuggestions,
                                     IReadOnlyList<string> RequiredConfirmations,
                                     IReadOnlyList<string> Warnings)
{
    public string SourceType => Format switch
    {
        StatementFormat.FnbCsv => "fnb_csv",
        StatementFormat.FnbZip => "fnb_zip",
        StatementFormat.StandardBankPdf => "standard_bank_pdf",
        _ => "generic_csv",
    };
}
