using System.IO.Compression;
using System.Text;

namespace argus.Server.Imports;

public static class CsvStatementParsers
{
    private static readonly string[] _delimiterNames = [",", ";", "\t", "|"];

    public static string DecodeUtf8(byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > ImportLimits.MaxFileBytes)
        {
            throw new StatementRejectedException(ParserReasons.FileTooLarge);
        }

        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedEncoding);
        }
    }

    public static (byte[] CsvBytes, StatementFormat Format) UnwrapFnbArchive(
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > ImportLimits.MaxFileBytes)
        {
            throw new StatementRejectedException(ParserReasons.FileTooLarge);
        }

        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K')
        {
            return (bytes, StatementFormat.FnbCsv);
        }

        try
        {
            using var archiveStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count != 1)
            {
                throw new StatementRejectedException(ParserReasons.UnsafeArchive);
            }

            var entry = archive.Entries[0];
            if (entry.FullName.Contains('/') || entry.FullName.Contains('\\') ||
                entry.FullName.Length == 0 || entry.FullName is "." or ".." ||
                !entry.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                entry.Length > ImportLimits.MaxDecompressedZipBytes)
            {
                throw new StatementRejectedException(ParserReasons.UnsafeArchive);
            }
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
            {
                throw new StatementRejectedException(ParserReasons.UnsafeArchive);
            }

            using var entryStream = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            var total = 0;
            int read;
            while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > ImportLimits.MaxDecompressedZipBytes)
                {
                    throw new StatementRejectedException(ParserReasons.UnsafeArchive);
                }
                output.Write(buffer, 0, read);
            }

            return (output.ToArray(), StatementFormat.FnbZip);
        }
        catch (StatementRejectedException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw new StatementRejectedException(ParserReasons.UnsafeArchive);
        }
        catch (IOException)
        {
            throw new StatementRejectedException(ParserReasons.UnsafeArchive);
        }
    }

    public static ParsedStatement ParseFnb(byte[] rawBytes,
                                           DateOnly? start,
                                           DateOnly? end,
                                           CancellationToken cancellationToken = default)
    {
        var (csvBytes, format) = UnwrapFnbArchive(rawBytes, cancellationToken);
        var text = DecodeUtf8(csvBytes, cancellationToken);
        var records = CsvRecordReader.Read(
            text, ',', ImportLimits.MaxDataRows + 100, cancellationToken);
        var header = records.FirstOrDefault(record => record.StartLine == 7);
        if (header is null || header.Malformed || header.Fields.Count != 4 ||
            !IsFnbHeader(header.Fields))
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        var dataRecords = records.Where(record => record.StartLine > 7).ToList();
        if (dataRecords.Count == 0)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedFormat);
        }
        if (dataRecords.Count > ImportLimits.MaxDataRows)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }

        var rows = new List<ParsedStatementRow>(dataRecords.Count);
        DateOnly? firstDate = null;
        DateOnly? lastDate = null;
        var rowNumber = 0;
        foreach (var record in dataRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var fields = record.Fields;
            var raw = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["date"] = fields.Count > 0 ? fields[0] : null,
                ["amount"] = fields.Count > 1 ? fields[1] : null,
                ["description"] = fields.Count > 3 ? fields[3] : null,
            };
            var validation = new List<string>();
            var warnings = new List<string>();

            if (record.Malformed || fields.Count != 4)
            {
                raw["expected_fields"] = "4";
                raw["actual_fields"] = fields.Count.ToString();
                validation.Add(ValidationCodes.InvalidFieldCount);
            }

            DateOnly? date = null;
            if (raw["date"] is { } dateText &&
                StatementDates.TryParseFullYearWithFormat(dateText, "yyyy/MM/dd", out var parsedDate))
            {
                date = parsedDate;
            }
            else
            {
                validation.Add(ValidationCodes.InvalidDate);
            }

            long? cents = null;
            if (raw["amount"] is { } amountText &&
                StatementAmounts.TryParseSignedDecimal(amountText, out var parsedCents))
            {
                cents = parsedCents;
            }
            else
            {
                validation.Add(ValidationCodes.InvalidAmount);
            }

            var (display, normalized, empty, tooLong) =
                StatementText.NormalizeDescription(raw["description"], collapseDisplay: false);
            if (empty)
            {
                warnings.Add(ValidationCodes.EmptyDescription);
            }
            if (tooLong)
            {
                validation.Add(ValidationCodes.DescriptionTooLong);
            }

            if (date is not null)
            {
                if (start is not null && end is not null &&
                    !StatementDates.InPeriod(date.Value, start.Value, end.Value))
                {
                    validation.Add(ValidationCodes.DateOutsidePeriod);
                }
                if (StatementDates.IsFuture(date.Value))
                {
                    warnings.Add(ValidationCodes.FutureDate);
                }
                firstDate = firstDate is null || date < firstDate ? date : firstDate;
                lastDate = lastDate is null || date > lastDate ? date : lastDate;
            }

            rows.Add(new ParsedStatementRow(
                Guid.NewGuid(),
                record.StartLine,
                null,
                rowNumber,
                raw,
                date,
                display,
                normalized,
                cents,
                validation.Distinct(StringComparer.Ordinal).ToArray(),
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                null,
                null));
        }

        return new ParsedStatement(
            format,
            rows,
            firstDate,
            lastDate,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["format"] = "fnb",
                ["header_line"] = "7",
                ["delimiter"] = ",",
                ["date_format"] = "yyyy/MM/dd",
                ["amount_mode"] = "signed",
                ["direction"] = "normal",
                ["dates_source"] = "derived",
            },
            ["mapping", "zar", "start_date", "end_date"],
            ["balance_ignored"]);
    }

    private static bool IsFnbHeader(IReadOnlyList<string> fields) =>
        fields.Count == 4 &&
        string.Equals(fields[0].Trim(), "Date", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(fields[1].Trim(), "Amount", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(fields[2].Trim(), "Balance", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(fields[3].Trim(), "Description", StringComparison.OrdinalIgnoreCase);

    public static ParsedStatement InspectGeneric(
        string text,
        CancellationToken cancellationToken = default)
    {
        var detection = DetectGeneric(text, cancellationToken);
        if (detection is null)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedFormat);
        }

        try
        {
            return ParseGeneric(
                text, detection.Config, null, null, detection.Suggestions, cancellationToken);
        }
        catch (StatementRejectedException exception) when (
            exception.Reason == ParserReasons.FormatConfirmationRequired)
        {
            return new ParsedStatement(
                StatementFormat.GenericCsv,
                [],
                null,
                null,
                detection.Suggestions,
                ["mapping", "zar", "start_date", "end_date"],
                ["mapping_confirmation_required"]);
        }
    }

    public static ParsedStatement ParseGeneric(byte[] rawBytes,
                                               ParserConfig config,
                                               DateOnly? start,
                                               DateOnly? end,
                                               CancellationToken cancellationToken = default) =>
        ParseGeneric(
            DecodeUtf8(rawBytes, cancellationToken), config, start, end, null, cancellationToken);

    private static ParsedStatement ParseGeneric(string text,
                                                ParserConfig config,
                                                DateOnly? start,
                                                DateOnly? end,
                                                IReadOnlyDictionary<string, string>? existingSuggestions,
                                                CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var delimiter = ResolveDelimiter(config.Delimiter);
        var decimalSeparator = ResolveDecimal(config.DecimalSeparator);
        var thousandsSeparator = ResolveThousands(config.ThousandsSeparator);
        if (delimiter is null || decimalSeparator is null || thousandsSeparator == "\u0000" ||
            config.HeaderLine is null || config.HeaderLine < 1 ||
            config.DateColumn is null || config.DateFormat is null)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        if (!_supportedDateFormats.Contains(config.DateFormat))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        var amountMode = config.AmountMode ?? "signed";
        if (amountMode is not ("signed" or "debit_credit"))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        var direction = config.Direction ?? "normal";
        if (direction is not ("normal" or "inverted"))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        var negativeStyle = config.NegativeStyle ?? "any";
        if (negativeStyle is not ("any" or "parentheses" or "trailing_minus" or "cr_dr" or
            "leading_minus"))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        if (config.DescriptionColumn is null && !config.ConfirmEmptyDescription)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        if ((config.StableIdColumn is null) != (config.SourceNamespace is null))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        if (amountMode == "signed" && config.AmountColumn is null)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }
        if (amountMode == "debit_credit" && (config.DebitColumn is null || config.CreditColumn is null))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        var records = CsvRecordReader.Read(
            text, delimiter.Value, ImportLimits.MaxDataRows + 100, cancellationToken);

        // An FNB preamble never becomes generic source data. Detect the first
        // plausible transaction header, including malformed/alternate FNB
        // headers, and refuse a mapping that starts before it.
        var preambleBoundary = FindPreambleBoundary(records);
        if (preambleBoundary is not null && config.HeaderLine.Value < preambleBoundary.Value)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        // The FNB preamble shape must be excluded independently of the selected
        // generic delimiter. A non-comma dialect can otherwise turn the
        // preamble lines into one-field data rows before any record-based
        // header heuristic sees them.
        var fnbPreambleEnd = FindFnbPreambleEnd(text);
        if (fnbPreambleEnd is not null && config.HeaderLine.Value < fnbPreambleEnd.Value)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }

        var header = records.FirstOrDefault(record => record.StartLine == config.HeaderLine.Value);
        if (header is not null && header.Malformed)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }
        if (header is null)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        var dataRecords = records.Where(record => record.StartLine > config.HeaderLine.Value).ToList();
        if (dataRecords.Count == 0)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedFormat);
        }
        if (dataRecords.Count > ImportLimits.MaxDataRows)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }

        var columnIndexes = new[]
        {
            config.DateColumn,
            config.DescriptionColumn,
            config.AmountColumn,
            config.DebitColumn,
            config.CreditColumn,
            config.StableIdColumn,
        };
        if (columnIndexes.Any(value => value is < 0))
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }
        var maxIndex = columnIndexes
            .Where(value => value is not null)
            .Max(value => value!.Value);
        if (maxIndex >= header.Fields.Count)
        {
            throw new StatementRejectedException(ParserReasons.FormatConfirmationRequired);
        }
        var headerWidth = header.Fields.Count;
        var rows = new List<ParsedStatementRow>(dataRecords.Count);
        DateOnly? firstDate = null;
        DateOnly? lastDate = null;
        var rowNumber = 0;
        var centuryRequired = false;
        foreach (var record in dataRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var fields = record.Fields;
            var raw = new Dictionary<string, string?>(StringComparer.Ordinal);
            string? Field(int? index) =>
                index is >= 0 && index.Value < fields.Count ? fields[index.Value] : null;

            raw["date"] = Field(config.DateColumn);
            if (amountMode == "signed")
            {
                raw["amount"] = Field(config.AmountColumn);
            }
            else
            {
                raw["debit"] = Field(config.DebitColumn);
                raw["credit"] = Field(config.CreditColumn);
            }
            if (config.DescriptionColumn is not null)
            {
                raw["description"] = Field(config.DescriptionColumn);
            }
            if (config.StableIdColumn is not null)
            {
                raw["source_id"] = Field(config.StableIdColumn);
            }

            var validation = new List<string>();
            var warnings = new List<string>();
            if (record.Malformed || fields.Count != headerWidth)
            {
                raw["expected_fields"] = headerWidth.ToString();
                raw["actual_fields"] = fields.Count.ToString();
                validation.Add(ValidationCodes.InvalidFieldCount);
            }

            DateOnly? date = null;
            var twoDigit = IsTwoDigitFormat(config.DateFormat);
            if (!twoDigit)
            {
                if (raw["date"] is { } fullDate &&
                    StatementDates.TryParseFullYearWithFormat(fullDate, config.DateFormat, out var parsed))
                {
                    date = parsed;
                }
                else
                {
                    validation.Add(ValidationCodes.InvalidDate);
                }
            }
            else if (raw["date"] is { } shortDate &&
                     StatementDates.TryParseTwoDigitYearWithFormat(
                         shortDate, config.DateFormat, out var day, out var month, out var year2))
            {
                if (start is null || end is null)
                {
                    if (config.Century is null)
                    {
                        warnings.Add(ValidationCodes.CenturyRequired);
                        centuryRequired = true;
                    }
                    else
                    {
                        var (resolved, _) = StatementDates.Resolve(
                            day, month, year2, null, null, config.Century);
                        date = resolved;
                    }
                }
                else
                {
                    var (resolved, multiple) = StatementDates.Resolve(
                        day, month, year2, start, end, config.Century);
                    date = resolved;
                    if (resolved is null)
                    {
                        validation.Add(multiple
                            ? ValidationCodes.CenturyRequired
                            : ValidationCodes.InvalidDate);
                    }
                }
            }
            else
            {
                validation.Add(ValidationCodes.InvalidDate);
            }

            long? cents = null;
            if (amountMode == "signed")
            {
                if (raw["amount"] is { } amountRaw &&
                    StatementAmounts.TryParseMagnitude(
                        amountRaw,
                        decimalSeparator,
                        thousandsSeparator,
                        out var magnitude,
                        out var hasPositive,
                        out var hasNegative))
                {
                    if (hasNegative && !MatchesNegativeStyle(amountRaw, negativeStyle))
                    {
                        validation.Add(ValidationCodes.InvalidSign);
                    }
                    var negative = hasNegative;
                    if (direction == "inverted")
                    {
                        negative = !negative;
                    }
                    cents = negative ? -magnitude : magnitude;
                    if (cents == 0)
                    {
                        cents = 0;
                    }
                }
                else
                {
                    validation.Add(raw["amount"] is null
                        ? ValidationCodes.MissingAmount
                        : ValidationCodes.InvalidAmount);
                }
            }
            else
            {
                var debitRaw = raw["debit"];
                var creditRaw = raw["credit"];
                var debitPopulated = !string.IsNullOrWhiteSpace(debitRaw);
                var creditPopulated = !string.IsNullOrWhiteSpace(creditRaw);
                if (debitPopulated && creditPopulated)
                {
                    validation.Add(ValidationCodes.MultipleAmounts);
                }
                else if (!debitPopulated && !creditPopulated)
                {
                    validation.Add(ValidationCodes.MissingAmount);
                }
                else
                {
                    var chosen = debitPopulated ? debitRaw : creditRaw;
                    if (StatementAmounts.TryParseMagnitude(
                            chosen,
                            decimalSeparator,
                            thousandsSeparator,
                            out var magnitude,
                            out _,
                            out _))
                    {
                        cents = debitPopulated ? -magnitude : magnitude;
                        if (cents == 0)
                        {
                            cents = 0;
                        }
                    }
                    else
                    {
                        validation.Add(ValidationCodes.InvalidAmount);
                    }
                }
            }

            var (display, normalized, empty, tooLong) =
                StatementText.NormalizeDescription(
                    raw.GetValueOrDefault("description"), collapseDisplay: false);
            if (empty)
            {
                warnings.Add(ValidationCodes.EmptyDescription);
            }
            if (tooLong)
            {
                validation.Add(ValidationCodes.DescriptionTooLong);
            }

            if (date is not null)
            {
                if (start is not null && end is not null &&
                    !StatementDates.InPeriod(date.Value, start.Value, end.Value))
                {
                    validation.Add(ValidationCodes.DateOutsidePeriod);
                }
                if (StatementDates.IsFuture(date.Value))
                {
                    warnings.Add(ValidationCodes.FutureDate);
                }
                firstDate = firstDate is null || date < firstDate ? date : firstDate;
                lastDate = lastDate is null || date > lastDate ? date : lastDate;
            }

            var sourceId = config.StableIdColumn is not null
                ? raw.GetValueOrDefault("source_id")
                : null;
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                sourceId = null;
            }

            rows.Add(new ParsedStatementRow(
                Guid.NewGuid(),
                record.StartLine,
                null,
                rowNumber,
                raw,
                date,
                display,
                normalized,
                cents,
                validation.Distinct(StringComparer.Ordinal).ToArray(),
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                sourceId is null ? null : config.SourceNamespace,
                sourceId));
        }

        var suggestions = existingSuggestions is null
            ? ConfigToSuggestions(config)
            : new Dictionary<string, string>(existingSuggestions, StringComparer.Ordinal);
        var required = new List<string> { "mapping", "zar", "start_date", "end_date" };
        if (centuryRequired || rows.Any(row => row.Warnings.Contains(ValidationCodes.CenturyRequired)))
        {
            required.Add("century");
        }

        return new ParsedStatement(
            StatementFormat.GenericCsv,
            rows,
            firstDate,
            lastDate,
            suggestions,
            required,
            []);
    }

    private static GenericDetection? DetectGeneric(
        string text,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<GenericCandidate>();
        foreach (var delimiter in _delimiterNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CsvRecord> records;
            try
            {
                records = CsvRecordReader.Read(
                    text, delimiter[0], ImportLimits.MaxDataRows + 100, cancellationToken);
            }
            catch (StatementRejectedException exception) when (
                exception.Reason == ParserReasons.ProcessingLimit)
            {
                // A wrong delimiter can turn quoted multiline content into many
                // physical records. Do not let that candidate reject the file
                // when another dialect parses it correctly.
                continue;
            }
            var sample = records.Take(50).ToList();
            if (sample.Count == 0)
            {
                continue;
            }
            var fieldCounts = sample.Select(record => record.Fields.Count).Where(count => count >= 2).ToList();
            if (fieldCounts.Count < 2)
            {
                continue;
            }
            var mostCommon = fieldCounts
                .GroupBy(count => count)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .First();
            var consistency = mostCommon.Count() * 10 - Math.Abs(mostCommon.Key - 4);
            candidates.Add(new GenericCandidate(delimiter[0], records, consistency, mostCommon.Key));
        }

        var best = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var header = best.Records.FirstOrDefault(record =>
            record.Fields.Count >= 3 && LooksLikeHeader(record.Fields));
        header ??= best.Records.FirstOrDefault(record => record.Fields.Count >= 3);
        if (header is null)
        {
            return null;
        }

        var headerFields = header.Fields.Select(NormalizeHeader).ToArray();
        var dateColumn = FindHeader(headerFields, "date", "transaction date", "value date", "posting date");
        var descriptionColumn = FindHeader(headerFields, "description", "details", "narrative", "memo", "reference");
        var amountColumn = FindHeader(headerFields, "amount", "value", "transaction amount");
        var debitColumn = FindHeader(headerFields, "debit", "withdrawal", "money out", "out");
        var creditColumn = FindHeader(headerFields, "credit", "deposit", "money in", "in");
        var stableIdColumn = FindHeader(headerFields, "transaction id", "id", "reference");
        if (stableIdColumn == descriptionColumn)
        {
            stableIdColumn = null;
        }

        dateColumn ??= 0;
        descriptionColumn ??= Math.Min(1, header.Fields.Count - 1);
        var amountMode = debitColumn is not null && creditColumn is not null
            ? "debit_credit"
            : "signed";
        if (amountMode == "signed")
        {
            amountColumn ??= header.Fields.Count > 2 ? 2 : null;
        }

        var dataRecords = best.Records
            .Where(record => record.StartLine > header.StartLine)
            .Take(40)
            .ToList();
        var dateFormat = ProposeDateFormat(dataRecords, dateColumn);
        var (decimalSeparator, thousandsSeparator) = ProposeSeparators(
            dataRecords,
            amountMode == "signed" ? amountColumn : debitColumn);

        var config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = header.StartLine,
            Delimiter = best.Delimiter.ToString(),
            DateFormat = dateFormat,
            DecimalSeparator = decimalSeparator,
            ThousandsSeparator = thousandsSeparator,
            AmountMode = amountMode,
            DateColumn = dateColumn,
            DescriptionColumn = descriptionColumn,
            AmountColumn = amountColumn,
            DebitColumn = debitColumn,
            CreditColumn = creditColumn,
            Direction = "normal",
            StableIdColumn = stableIdColumn,
            SourceNamespace = stableIdColumn is null ? null : "generic",
            ConfirmEmptyDescription = descriptionColumn is null,
        };

        var suggestions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["format"] = "generic",
            ["header_line"] = header.StartLine.ToString(),
            ["delimiter"] = best.Delimiter.ToString(),
            ["date_format"] = dateFormat,
            ["decimal_separator"] = decimalSeparator,
            ["thousands_separator"] = thousandsSeparator,
            ["amount_mode"] = amountMode,
            ["date_column"] = dateColumn!.Value.ToString(),
            ["description_column"] = descriptionColumn?.ToString() ?? string.Empty,
            ["amount_column"] = amountColumn?.ToString() ?? string.Empty,
            ["debit_column"] = debitColumn?.ToString() ?? string.Empty,
            ["credit_column"] = creditColumn?.ToString() ?? string.Empty,
            ["direction"] = "normal",
            ["negative_style"] = "any",
            ["stable_id_column"] = stableIdColumn?.ToString() ?? string.Empty,
            ["source_namespace"] = stableIdColumn is null ? string.Empty : "generic",
            ["dates_source"] = "derived",
        };
        return new GenericDetection(config, suggestions);
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> fields)
    {
        var normalized = fields.Select(NormalizeHeader).ToArray();
        var hasDate = normalized.Any(value => value.Contains("date", StringComparison.Ordinal));
        var hasDescription = normalized.Any(value =>
            value.Contains("desc", StringComparison.Ordinal) ||
            value.Contains("detail", StringComparison.Ordinal) ||
            value.Contains("narrative", StringComparison.Ordinal) ||
            value.Contains("memo", StringComparison.Ordinal));
        var hasAmount = normalized.Any(value =>
            value.Contains("amount", StringComparison.Ordinal) ||
            value.Contains("debit", StringComparison.Ordinal) ||
            value.Contains("credit", StringComparison.Ordinal));
        return hasDate && hasDescription && hasAmount;
    }

    private static string NormalizeHeader(string value) =>
        StatementText.CollapseWhitespace(value).ToLowerInvariant();

    private static int? FindHeader(IReadOnlyList<string> headers, params string[] names)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (names.Contains(headers[index], StringComparer.Ordinal))
            {
                return index;
            }
        }
        return null;
    }

    private static string ProposeDateFormat(IReadOnlyList<CsvRecord> records, int? column)
    {
        if (column is null)
        {
            return "yyyy-MM-dd";
        }

        var values = records
            .Where(record => column.Value < record.Fields.Count)
            .Select(record => record.Fields[column.Value])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(20)
            .ToList();
        foreach (var format in _preferredDateFormats)
        {
            if (values.Count > 0 && values.All(value =>
                    format.Contains("yy", StringComparison.Ordinal) &&
                    !format.Contains("yyyy", StringComparison.Ordinal)
                        ? StatementDates.TryParseTwoDigitYearWithFormat(value, format, out _, out _, out _)
                        : StatementDates.TryParseFullYearWithFormat(value, format, out _)))
            {
                return format;
            }
        }
        return "yyyy-MM-dd";
    }

    private static (string Decimal, string Thousands) ProposeSeparators(
        IReadOnlyList<CsvRecord> records,
        int? column)
    {
        if (column is null)
        {
            return (".", "none");
        }

        var values = records
            .Where(record => column.Value < record.Fields.Count)
            .Select(record => record.Fields[column.Value])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(20)
            .ToList();
        foreach (var value in values)
        {
            var lastComma = value.LastIndexOf(',');
            var lastDot = value.LastIndexOf('.');
            if (lastComma >= 0 && lastDot >= 0)
            {
                return lastDot > lastComma ? (".", "comma") : (",", "dot");
            }
        }
        return (".", "none");
    }

    private static bool MatchesNegativeStyle(string raw, string style)
    {
        var value = raw.Trim();
        return style switch
        {
            "any" => true,
            "leading_minus" => value.StartsWith('-'),
            "parentheses" => value.StartsWith('(') && value.EndsWith(')'),
            "trailing_minus" => value.EndsWith('-'),
            "cr_dr" => value.EndsWith("DR", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static Dictionary<string, string> ConfigToSuggestions(ParserConfig config)
    {
        var suggestions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["format"] = "generic",
            ["header_line"] = config.HeaderLine?.ToString() ?? string.Empty,
            ["delimiter"] = config.Delimiter ?? string.Empty,
            ["date_format"] = config.DateFormat ?? string.Empty,
            ["decimal_separator"] = config.DecimalSeparator ?? string.Empty,
            ["thousands_separator"] = config.ThousandsSeparator ?? string.Empty,
            ["amount_mode"] = config.AmountMode ?? string.Empty,
            ["date_column"] = config.DateColumn?.ToString() ?? string.Empty,
            ["description_column"] = config.DescriptionColumn?.ToString() ?? string.Empty,
            ["amount_column"] = config.AmountColumn?.ToString() ?? string.Empty,
            ["debit_column"] = config.DebitColumn?.ToString() ?? string.Empty,
            ["credit_column"] = config.CreditColumn?.ToString() ?? string.Empty,
            ["direction"] = config.Direction ?? string.Empty,
            ["negative_style"] = config.NegativeStyle ?? "any",
            ["stable_id_column"] = config.StableIdColumn?.ToString() ?? string.Empty,
            ["source_namespace"] = config.SourceNamespace ?? string.Empty,
            ["century"] = config.Century?.ToString() ?? string.Empty,
        };
        return suggestions;
    }

    private static int? FindPreambleBoundary(IReadOnlyList<CsvRecord> records)
    {
        foreach (var record in records.Take(20))
        {
            if (record.Fields.Count < 3)
            {
                continue;
            }
            var fields = record.Fields
                .Select(field => StatementText.CollapseWhitespace(field).ToLowerInvariant())
                .ToArray();
            var hasDate = fields.Any(field =>
                field.Contains("date", StringComparison.Ordinal));
            var hasDescription = fields.Any(field =>
                field.Contains("description", StringComparison.Ordinal) ||
                field.Contains("details", StringComparison.Ordinal) ||
                field.Contains("narrative", StringComparison.Ordinal) ||
                field.Contains("memo", StringComparison.Ordinal));
            var hasAmount = fields.Any(field =>
                field.Contains("amount", StringComparison.Ordinal) ||
                field.Contains("balance", StringComparison.Ordinal) ||
                field.Contains("debit", StringComparison.Ordinal) ||
                field.Contains("credit", StringComparison.Ordinal));
            if (hasDate && hasDescription && hasAmount)
            {
                return record.StartLine;
            }
        }
        return null;
    }

    private static int? FindFnbPreambleEnd(string text)
    {
        var lines = text.Split('\n')
            .Take(10)
            .Select(line => line.TrimEnd('\r'))
            .ToList();
        var markerCount = lines.Take(6).Count(LooksLikeFnbPreambleLine);
        if (markerCount < 3)
        {
            return null;
        }

        for (var index = 6; index < lines.Count; index++)
        {
            if (LooksLikeTransactionHeader(lines[index]))
            {
                return index + 1;
            }
        }

        // A malformed or unrecognizable transaction header can still follow a
        // real FNB preamble. Only an explicit FNB title establishes that
        // boundary; otherwise these markers may be ordinary description-first
        // generic data and must not reject a valid mapping.
        return lines.Count > 0 && LooksLikeFnbTitle(lines[0]) ? 7 : null;
    }

    private static bool LooksLikeFnbTitle(string line)
    {
        var normalized = StatementText.CollapseWhitespace(
            line.TrimStart().TrimStart('"')).ToLowerInvariant();
        return normalized.Contains("fnb", StringComparison.Ordinal) ||
               normalized.Contains("first national", StringComparison.Ordinal);
    }

    private static bool LooksLikeFnbPreambleLine(string line)
    {
        var normalized = StatementText.CollapseWhitespace(
            line.TrimStart().TrimStart('"')).ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }
        return normalized.StartsWith("fnb", StringComparison.Ordinal) ||
               normalized.StartsWith("first national", StringComparison.Ordinal) ||
               normalized.StartsWith("account", StringComparison.Ordinal) ||
               normalized.StartsWith("name", StringComparison.Ordinal) ||
               normalized.StartsWith("balance", StringComparison.Ordinal) ||
               normalized.StartsWith("statement period", StringComparison.Ordinal) ||
               normalized.StartsWith("statement date", StringComparison.Ordinal) ||
               normalized.StartsWith("branch", StringComparison.Ordinal) ||
               normalized.StartsWith("customer", StringComparison.Ordinal) ||
               normalized.StartsWith("page", StringComparison.Ordinal) ||
               normalized.StartsWith("do not use", StringComparison.Ordinal);
    }

    private static bool LooksLikeTransactionHeader(string line)
    {
        var normalized = StatementText.CollapseWhitespace(line).ToLowerInvariant();
        return normalized.Contains("date", StringComparison.Ordinal) &&
               (normalized.Contains("desc", StringComparison.Ordinal) ||
                normalized.Contains("details", StringComparison.Ordinal) ||
                normalized.Contains("narrative", StringComparison.Ordinal) ||
                normalized.Contains("memo", StringComparison.Ordinal)) &&
               (normalized.Contains("amount", StringComparison.Ordinal) ||
                normalized.Contains("debit", StringComparison.Ordinal) ||
                normalized.Contains("credit", StringComparison.Ordinal) ||
                normalized.Contains("balance", StringComparison.Ordinal));
    }

    private static bool IsTwoDigitFormat(string format) =>
        format.Contains("yy", StringComparison.Ordinal) &&
        !format.Contains("yyyy", StringComparison.Ordinal);

    private static char? ResolveDelimiter(string? value) => value switch
    {
        "," or "comma" => ',',
        ";" or "semicolon" => ';',
        "\t" or "tab" => '\t',
        "|" or "pipe" => '|',
        _ => null,
    };

    private static string? ResolveDecimal(string? value) => value switch
    {
        "." or "dot" => ".",
        "," or "comma" => ",",
        _ => null,
    };

    private static string ResolveThousands(string? value) => value switch
    {
        null or "none" or "" => string.Empty,
        "," or "comma" => ",",
        "." or "dot" => ".",
        " " or "space" => " ",
        _ => "\u0000",
    };

    private static readonly string[] _preferredDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "dd MMM yyyy",
        "dd/MM/yy",
        "dd MMM yy",
    ];

    private static readonly HashSet<string> _supportedDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "d/M/yyyy",
        "M/d/yyyy",
        "dd MMM yyyy",
        "d MMM yyyy",
        "dd/MM/yy",
        "MM/dd/yy",
        "d/M/yy",
        "M/d/yy",
        "dd MMM yy",
        "d MMM yy",
        "yy-MM-dd",
        "yy/MM/dd",
    ];

    private sealed record GenericCandidate(char Delimiter,
                                           IReadOnlyList<CsvRecord> Records,
                                           int Score,
                                           int CommonFieldCount);

    private sealed record GenericDetection(
        ParserConfig Config,
        IReadOnlyDictionary<string, string> Suggestions);
}
