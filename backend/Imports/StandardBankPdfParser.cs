using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace argus.Server.Imports;

/// <summary>
/// Positional Standard Bank six-column parser. It never reads page text in
/// reading order, never returns account metadata, preamble, filenames, or
/// balances, and maps every failure to a fixed safe reason.
/// </summary>
public static class StandardBankPdfParser
{
    private static readonly Regex _dateAnchor = new(
        @"^(\d{1,2})\s+([A-Za-z]{3})\s+(\d{2}|\d{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _periodLine = new(
        @"^(?:statement\s+period\s*:?\s*|period\s+from\s+)?(\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4})\s*" +
        @"(?:to|until|through|[-–])\s*(\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _coverPhrase = new(
        @"^synthetic\s+(?:bank|statement)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _summaryHeading = new(
        @"^(?:statement\s+summary|account\s+summary|summary)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _footerPage = new(
        @"^page\s+(\d+)(?:\s+of\s+(\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _footerContinued = new(
        @"^continued\s+on\s+page\s+(\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _numericToken = new(
        @"^\(?-?\d{1,3}(,\d{3})*(\.\d{2})?\)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] _flowColumns = ["Payments", "Deposits", "Bank fees"];

    private static readonly (string Label, int FlowIndex)[] _totalLabels =
    [
        ("total payments", 0),
        ("total deposits", 1),
        ("total bank fees", 2),
    ];

    private static readonly string[] _openingBalanceLabels =
    [
        "opening balance",
        "closing balance brought forward",
        "balance brought forward",
        "brought forward",
    ];

    private static readonly string[] _closingBalanceLabels =
    [
        "closing balance",
        "closing balance carried forward",
        "balance carried forward",
        "carried forward",
    ];

    private static readonly string[] _metadataLabels =
    [
        "account", "account:", "customer", "customer:", "branch", "branch:",
    ];

    // Footer text is recognised only in the bottom margin. This is a
    // classification position, not a traversal cutoff: anything beside or
    // below a footer is still visited and must be classified or rejected.
    private const double FooterMaxY = 60.0;
    private const double ContinuationLeftTolerance = 12.0;
    private const double ContinuationVerticalTolerance = 12.0;

    // The supported cross-page bridge is a leading table slot: the fixture
    // header is at Y=800 and its first continuation at Y=770.
    private const double LeadingContinuationMaxGap = 80.0;

    // Same-page repeated headers may differ only inside the fixture-backed
    // tolerance, and only when no word changes semantic columns.
    private const double HeaderCompatibilityTolerance = 6.0;
    private const double HeaderExactTolerance = 0.01;

    // The inspected masthead/period fixtures sit around Y=780-820 on an
    // 842-point page. Y=400 is not a top-of-page role.
    private const double TopRegionBelowPageTop = 120.0;

    public static ParsedStatement Parse(byte[] bytes,
                                        DateOnly? start,
                                        DateOnly? end,
                                        int? century)
    {
        if (bytes.Length > ImportLimits.MaxFileBytes)
        {
            throw new StatementRejectedException(ParserReasons.FileTooLarge);
        }

        // Reject encrypted documents before the extraction library gets a chance
        // to open them with an empty password.
        if (bytes.AsSpan().IndexOf("/Encrypt"u8) >= 0)
        {
            throw new StatementRejectedException(ParserReasons.EncryptedPdf);
        }

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes, new ParsingOptions
            {
                UseLenientParsing = false,
                SkipMissingFonts = false,
            });
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new StatementRejectedException(ParserReasons.EncryptedPdf);
        }
        catch (Exception)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        using (document)
        {
            try
            {
                return ParseDocument(document, start, end, century);
            }
            catch (StatementRejectedException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }
    }

    private static ParsedStatement ParseDocument(PdfDocument document,
                                                 DateOnly? start,
                                                 DateOnly? end,
                                                 int? century)
    {
        if (document.IsEncrypted)
        {
            throw new StatementRejectedException(ParserReasons.EncryptedPdf);
        }
        if (document.NumberOfPages > ImportLimits.MaxPdfPages)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }

        var pageCount = document.NumberOfPages;
        var events = new List<BodyEvent>();
        var candidateDrafts = new List<PdfRowDraft>();
        var totalCharacters = 0;
        var sawAnyText = false;
        var sawTable = false;
        DateOnly? printedStart = null;
        DateOnly? printedEnd = null;
        var periodConflict = false;
        var documentLine = 0;
        var eventIndex = 0;
        PdfHeader? previousHeader = null;
        PdfRowDraft? current = null;

        foreach (var page in document.GetPages())
        {
            // The supported subset is text-only. Check every page before any
            // cover/table branch: no header, marker or small logo is exempt.
            if (page.NumberOfImages > 0)
            {
                throw new StatementRejectedException(ParserReasons.TextPdfRequired);
            }

            var words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new PdfWord(
                    word.Text,
                    word.BoundingBox.Left,
                    word.BoundingBox.Right,
                    word.BoundingBox.Bottom,
                    word.BoundingBox.Top))
                .ToList();
            totalCharacters += words.Sum(word => word.Text.Length);
            if (totalCharacters > ImportLimits.MaxExtractedCharacters)
            {
                throw new StatementRejectedException(ParserReasons.ProcessingLimit);
            }
            sawAnyText |= words.Count > 0;

            // Every extracted word must belong to a mapped cell. The six bands
            // cover [0, infinity); an off-page or non-finite coordinate is not
            // silently dropped.
            if (words.Any(word =>
                    !double.IsFinite(word.CenterX) ||
                    !double.IsFinite(word.CenterY) ||
                    word.CenterX < 0))
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            var lines = GroupLines(words);
            foreach (var line in lines)
            {
                line.SourceLine = ++documentLine;
            }

            var headerMatches = FindHeaders(lines);
            if (headerMatches.Count == 0)
            {
                // A headerless page is harmless only when every line is a
                // complete, independently recognised cover/summary/footer
                // structure at its approved position. One marker cannot excuse
                // the rest of the page.
                previousHeader = null;
                current = null;
                foreach (var line in lines)
                {
                    if (TryConsumePageFooter(line, page.Number, pageCount))
                    {
                        continue;
                    }

                    var text = StatementText.CollapseWhitespace(line.Text);
                    if (_coverPhrase.IsMatch(text) || _summaryHeading.IsMatch(text) ||
                        TryConsumePeriodLine(
                            text, ref printedStart, ref printedEnd, ref periodConflict))
                    {
                        if (!IsTopOfPage(line, page.Height))
                        {
                            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                        }
                        events.Add(new BodyEvent(eventIndex++, BodyEventKind.Structural, null));
                        continue;
                    }

                    throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                }
                continue;
            }

            var primary = headerMatches[0];
            var header = primary.Header;
            var headerLineIndices = headerMatches
                .Select(match => match.LineIndex)
                .ToHashSet();
            foreach (var other in headerMatches.Skip(1))
            {
                if (!HeadersAreCompatible(primary.Header, other.Header, lines, headerLineIndices))
                {
                    throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                }
            }

            var headerWithinTolerance =
                previousHeader is not null && header.IsCompatibleWith(previousHeader);
            if (headerWithinTolerance && current is not null &&
                !HeadersAreCompatible(previousHeader!, header, lines, headerLineIndices))
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            var continuing = headerWithinTolerance && current is not null;
            if (!continuing)
            {
                current = null;
            }
            var crossPageContinuation = continuing;
            previousHeader = header;
            sawTable = true;
            var samePageContinuationClosed = false;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                if (headerLineIndices.Contains(lineIndex))
                {
                    events.Add(new BodyEvent(eventIndex++, BodyEventKind.Structural, null));
                    continue;
                }
                if (TryConsumePageFooter(line, page.Number, pageCount))
                {
                    // A footer ends same-page continuation eligibility; it does
                    // not discard a trailing row that may continue next page.
                    samePageContinuationClosed = true;
                    continue;
                }

                if (lineIndex < primary.LineIndex)
                {
                    var preambleText = StatementText.CollapseWhitespace(line.Text);
                    if (_summaryHeading.IsMatch(preambleText))
                    {
                        if (!IsTopOfPage(line, page.Height))
                        {
                            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                        }
                        events.Add(new BodyEvent(eventIndex++, BodyEventKind.Structural, null));
                        current = null;
                        crossPageContinuation = false;
                        continue;
                    }
                    if (_coverPhrase.IsMatch(preambleText) ||
                        TryConsumePeriodLine(
                            preambleText, ref printedStart, ref printedEnd, ref periodConflict))
                    {
                        if (!IsTopOfPage(line, page.Height))
                        {
                            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                        }
                        // Table masthead/period stays part of this table; it
                        // does not close a supported next-page bridge.
                        events.Add(new BodyEvent(eventIndex++, BodyEventKind.Structural, null));
                        continue;
                    }
                    if (IsMetadataLabelLine(line))
                    {
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }

                    var preambleDate = line.TextIn(header, 0);
                    var preambleDescription = line.TextIn(header, 1);
                    var preambleFlows = FlowTexts(line, header);
                    var preambleBalance = line.TextIn(header, 5);
                    var hasFlow = preambleFlows.Any(value => value.Length > 0);
                    if (preambleDate.Length > 0 && hasFlow)
                    {
                        // A pre-header row is retained only when both a date
                        // cell and a flow cell make it an unambiguous candidate.
                        var draft = AddCandidate(
                            line, page.Number, preambleDate, preambleDescription,
                            preambleFlows, start, end, century, ref eventIndex,
                            events, candidateDrafts);
                        current = draft;
                        crossPageContinuation = false;
                        samePageContinuationClosed = false;
                        continue;
                    }
                    if (preambleDescription.Length > 0 && preambleBalance.Length == 0)
                    {
                        if (!crossPageContinuation || current is null ||
                            !IsSupportedContinuation(line, header, current, crossPage: true))
                        {
                            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                        }
                        current.AppendContinuation(preambleDescription, line.CenterY);
                        crossPageContinuation = false;
                        continue;
                    }

                    throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                }

                var dateText = line.TextIn(header, 0);
                var descriptionText = line.TextIn(header, 1);
                var flowTexts = FlowTexts(line, header);
                var balanceText = line.TextIn(header, 5);

                if (IsTerminalTotal(dateText, descriptionText, flowTexts, balanceText))
                {
                    var total = NewDraft(
                        line.SourceLine, page.Number, dateText, null, descriptionText,
                        flowTexts, line.CenterY, start, end, century);
                    events.Add(new BodyEvent(eventIndex++, BodyEventKind.Total, total));
                    current = null;
                    crossPageContinuation = false;
                    samePageContinuationClosed = false;
                    continue;
                }

                if (dateText.Length > 0 || flowTexts.Any(value => value.Length > 0))
                {
                    var draft = AddCandidate(
                        line, page.Number, dateText, descriptionText, flowTexts,
                        start, end, century, ref eventIndex, events, candidateDrafts);
                    current = draft;
                    crossPageContinuation = false;
                    samePageContinuationClosed = false;
                    continue;
                }

                if (descriptionText.Length > 0)
                {
                    if (balanceText.Length > 0)
                    {
                        if (TryClassifyBalance(descriptionText, balanceText, out var isOpening))
                        {
                            events.Add(new BodyEvent(
                                eventIndex++,
                                isOpening
                                    ? BodyEventKind.BalanceOpening
                                    : BodyEventKind.BalanceClosing,
                                null));
                            current = null;
                            crossPageContinuation = false;
                            samePageContinuationClosed = false;
                            continue;
                        }

                        // A description and a balance cell that is not a complete
                        // balance row is unexplained text.
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }

                    if (current is null || samePageContinuationClosed ||
                        !IsSupportedContinuation(line, header, current, crossPageContinuation))
                    {
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }
                    current.AppendContinuation(descriptionText, line.CenterY);
                    crossPageContinuation = false;
                    continue;
                }

                // A balance-only line, or a line whose text fell outside every
                // recognised structure, is not safe to ignore.
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }

        if (!sawAnyText || !sawTable)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        var candidateEvents = events
            .Where(item => item.Kind == BodyEventKind.Candidate)
            .ToList();
        if (candidateEvents.Count == 0)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        // A provisional total is terminal only when every later nonblank role
        // is a permitted total, allowed closing balance or consistent footer.
        // A later structural role (cover/summary/period/header/restart),
        // candidate or opening balance disqualifies it.
        var lastDisqualifyingIndex = events
            .Where(item => item.Kind is
                BodyEventKind.Candidate or
                BodyEventKind.BalanceOpening or
                BodyEventKind.Structural)
            .Select(item => item.Index)
            .DefaultIfEmpty(-1)
            .Max();

        var finalEvents = events
            .Where(item =>
                item.Kind == BodyEventKind.Candidate ||
                (item.Kind == BodyEventKind.Total && item.Index < lastDisqualifyingIndex))
            .OrderBy(item => item.Index)
            .ToList();
        if (finalEvents.Count == 0)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }
        if (finalEvents.Count > ImportLimits.MaxDataRows)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }

        var firstFinalCandidate = finalEvents[0].Index;
        var lastFinalCandidate = finalEvents[^1].Index;
        foreach (var balance in events.Where(item =>
                     item.Kind is BodyEventKind.BalanceOpening or BodyEventKind.BalanceClosing))
        {
            if (balance.Kind == BodyEventKind.BalanceOpening &&
                balance.Index > firstFinalCandidate)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            if (balance.Kind == BodyEventKind.BalanceClosing &&
                balance.Index < lastFinalCandidate)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }

        var parsedRows = new List<ParsedStatementRow>(finalEvents.Count);
        for (var index = 0; index < finalEvents.Count; index++)
        {
            var draft = finalEvents[index].Draft!;
            draft.SourceRowNumber = index + 1;
            parsedRows.Add(draft.Build());
        }

        var proposedStart = periodConflict ? null : printedStart;
        var proposedEnd = periodConflict ? null : printedEnd;
        if (proposedStart is null || proposedEnd is null)
        {
            proposedStart = null;
            proposedEnd = null;
        }

        return new ParsedStatement(
            StatementFormat.StandardBankPdf,
            parsedRows,
            proposedStart,
            proposedEnd,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["format"] = "standard_bank",
                ["source_kind"] = "standard_bank_pdf",
                ["dates_source"] = proposedStart is null ? "missing" : "printed",
            },
            ["mapping", "zar", "start_date", "end_date"],
            ["balance_ignored"]);
    }

    private static PdfRowDraft AddCandidate(PdfLine line,
                                            int pageNumber,
                                            string dateText,
                                            string descriptionText,
                                            string[] flowTexts,
                                            DateOnly? start,
                                            DateOnly? end,
                                            int? century,
                                            ref int eventIndex,
                                            List<BodyEvent> events,
                                            List<PdfRowDraft> candidateDrafts)
    {
        var dateMatch = _dateAnchor.Match(dateText);
        var draft = NewDraft(
            line.SourceLine,
            pageNumber,
            dateText,
            dateMatch.Success ? dateMatch : null,
            descriptionText,
            flowTexts,
            line.CenterY,
            start,
            end,
            century);
        candidateDrafts.Add(draft);
        if (candidateDrafts.Count > ImportLimits.MaxDataRows)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }
        events.Add(new BodyEvent(eventIndex++, BodyEventKind.Candidate, draft));
        return draft;
    }

    private static PdfRowDraft NewDraft(int sourceLine,
                                        int sourcePage,
                                        string rawDate,
                                        Match? dateMatch,
                                        string description,
                                        string[] flows,
                                        double lineCenterY,
                                        DateOnly? start,
                                        DateOnly? end,
                                        int? century) =>
        new(sourceLine, sourcePage, rawDate, dateMatch, description, flows, lineCenterY, start, end, century);

    private static bool IsTerminalTotal(string dateText,
                                        string descriptionText,
                                        string[] flowTexts,
                                        string balanceText)
    {
        if (dateText.Length > 0 || balanceText.Length > 0)
        {
            return false;
        }

        var normalized = StatementText.CollapseWhitespace(descriptionText).ToLowerInvariant();
        var expected = -1;
        foreach (var label in _totalLabels)
        {
            if (normalized == label.Label)
            {
                expected = label.FlowIndex;
                break;
            }
        }
        if (expected < 0)
        {
            return false;
        }

        var populated = flowTexts
            .Select((value, index) => (value, index))
            .Where(entry => entry.value.Length > 0)
            .ToList();
        if (populated.Count != 1 || populated[0].index != expected)
        {
            return false;
        }

        var tokens = populated[0].value.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 1 &&
               StatementAmounts.TryParseMagnitude(tokens[0], ".", ",", out _, out _, out _);
    }

    private static bool TryClassifyBalance(
        string descriptionText,
        string balanceText,
        out bool isOpening)
    {
        isOpening = false;
        var normalized = StatementText.CollapseWhitespace(descriptionText).ToLowerInvariant();
        var opening = _openingBalanceLabels.Contains(normalized, StringComparer.Ordinal);
        var closing = _closingBalanceLabels.Contains(normalized, StringComparer.Ordinal);
        if (!opening && !closing)
        {
            return false;
        }

        var tokens = balanceText.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 1 || !_numericToken.IsMatch(tokens[0]))
        {
            return false;
        }

        isOpening = opening;
        return true;
    }

    private static bool TryConsumePeriodLine(string text,
                                             ref DateOnly? printedStart,
                                             ref DateOnly? printedEnd,
                                             ref bool periodConflict)
    {
        var match = _periodLine.Match(text);
        if (!match.Success)
        {
            return false;
        }

        if (!TryParsePeriodDate(match.Groups[1].Value, out var start) ||
            !TryParsePeriodDate(match.Groups[2].Value, out var end) ||
            end < start)
        {
            // The complete period structure is consumed, but an invalid printed
            // period does not prefill anything.
            return true;
        }

        if (printedStart is not null &&
            (printedStart != start || printedEnd != end))
        {
            periodConflict = true;
        }
        printedStart = start;
        printedEnd = end;
        return true;
    }

    private static bool TryConsumePageFooter(PdfLine line, int pageNumber, int pageCount)
    {
        if (line.CenterY > FooterMaxY)
        {
            return false;
        }

        var text = StatementText.CollapseWhitespace(line.Text).ToLowerInvariant();
        var pageMatch = _footerPage.Match(text);
        if (pageMatch.Success)
        {
            var number = int.Parse(pageMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (number != pageNumber)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            if (pageMatch.Groups[2].Success)
            {
                var total = int.Parse(pageMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                if (total != pageCount)
                {
                    throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                }
            }
            return true;
        }

        var continued = _footerContinued.Match(text);
        if (continued.Success)
        {
            var number = int.Parse(continued.Groups[1].Value, CultureInfo.InvariantCulture);
            if (number != pageNumber + 1 || number > pageCount)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            return true;
        }

        return false;
    }

    private static bool IsTopOfPage(PdfLine line, double pageHeight) =>
        line.CenterY >= pageHeight - TopRegionBelowPageTop;

    private static bool IsMetadataLabelLine(PdfLine line)
    {
        var first = line.Words.OrderBy(word => word.Left).FirstOrDefault();
        if (first is null)
        {
            return false;
        }
        var label = StatementText.CollapseWhitespace(first.Text).ToLowerInvariant();
        return _metadataLabels.Contains(label, StringComparer.Ordinal);
    }

    private static bool IsSupportedContinuation(PdfLine line,
                                                PdfHeader header,
                                                PdfRowDraft current,
                                                bool crossPage)
    {
        if (line.Words.Count == 0 ||
            !line.Words.All(word => header.InColumn(word, 1)))
        {
            return false;
        }

        var left = line.Words.Min(word => word.Left);
        if (Math.Abs(left - header.DescriptionLeft) > ContinuationLeftTolerance)
        {
            return false;
        }

        if (crossPage)
        {
            // The bridge is only the approved leading slot immediately below
            // the header, not a page-wide spacing waiver.
            var leadingGap = header.CenterY - line.CenterY;
            return leadingGap > 0 && leadingGap <= LeadingContinuationMaxGap;
        }

        var gap = current.LastLineCenterY - line.CenterY;
        return gap > 0 && gap <= ContinuationVerticalTolerance;
    }

    private static string[] FlowTexts(PdfLine line, PdfHeader header) =>
    [
        line.TextIn(header, 2),
        line.TextIn(header, 3),
        line.TextIn(header, 4),
    ];

    private static List<PdfLine> GroupLines(IReadOnlyList<PdfWord> words)
    {
        var lines = new List<PdfLine>();
        foreach (var word in words.OrderByDescending(word => word.CenterY).ThenBy(word => word.Left))
        {
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(line.CenterY - word.CenterY) <= 2.0)
            {
                line.Add(word);
            }
            else
            {
                lines.Add(new PdfLine(word));
            }
        }

        foreach (var line in lines)
        {
            line.Sort();
        }
        return lines.OrderByDescending(line => line.CenterY).ToList();
    }

    private static List<HeaderMatch> FindHeaders(IReadOnlyList<PdfLine> lines)
    {
        var matches = new List<HeaderMatch>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var words = line.Words.OrderBy(word => word.Left).ToList();
            var normalized = words.Select(word => NormalizeLabel(word.Text)).ToList();
            if (!TryGetHeaderIndices(normalized, out var dateIndex, out var descriptionIndex,
                    out var paymentsIndex, out var depositsIndex, out var bankIndex,
                    out var feesIndex, out var balanceIndex))
            {
                continue;
            }

            var date = words[dateIndex];
            var description = words[descriptionIndex];
            var payments = words[paymentsIndex];
            var deposits = words[depositsIndex];
            var bank = words[bankIndex];
            var fees = feesIndex == bankIndex ? bank : words[feesIndex];
            var balance = words[balanceIndex];
            var ordered = new List<PdfWord>
            {
                date, description, payments, deposits, bank,
            };
            if (feesIndex != bankIndex)
            {
                ordered.Add(fees);
            }
            ordered.Add(balance);

            var nonOverlapping = true;
            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index - 1].Left >= ordered[index].Left ||
                    ordered[index - 1].Right > ordered[index].Left)
                {
                    nonOverlapping = false;
                    break;
                }
            }
            if (!nonOverlapping)
            {
                continue;
            }

            var boundaries = new double[5];
            boundaries[0] = Midpoint(date.Right, description.Left);
            boundaries[1] = Midpoint(description.Right, payments.Left);
            boundaries[2] = Midpoint(payments.Right, deposits.Left);
            boundaries[3] = Midpoint(deposits.Right, bank.Left);
            boundaries[4] = Midpoint(Math.Max(bank.Right, fees.Right), balance.Left);
            matches.Add(new HeaderMatch(
                lineIndex,
                new PdfHeader(boundaries, description.Left, line.CenterY)));
        }
        return matches;
    }

    private static bool HeadersAreCompatible(PdfHeader primary,
                                             PdfHeader other,
                                             IReadOnlyList<PdfLine> lines,
                                             HashSet<int> headerLineIndices)
    {
        if (!primary.IsCompatibleWith(other))
        {
            return false;
        }

        var exact = primary.Boundaries
            .Zip(other.Boundaries, (left, right) => Math.Abs(left - right) <= HeaderExactTolerance)
            .All(value => value) &&
            Math.Abs(primary.DescriptionLeft - other.DescriptionLeft) <= HeaderExactTolerance;
        if (exact)
        {
            return true;
        }

        // A tolerated shift is safe only when no word would change semantic
        // columns. Tolerance cannot change a flow direction or turn Balance
        // into a flow.
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            if (headerLineIndices.Contains(lineIndex))
            {
                continue;
            }
            foreach (var word in lines[lineIndex].Words)
            {
                if (ColumnOf(primary, word) != ColumnOf(other, word))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static int ColumnOf(PdfHeader header, PdfWord word)
    {
        for (var column = 0; column < 6; column++)
        {
            var left = column == 0 ? 0 : header.Boundaries[column - 1];
            var right = column == 5 ? double.PositiveInfinity : header.Boundaries[column];
            if (word.CenterX >= left && word.CenterX < right)
            {
                return column;
            }
        }
        return -1;
    }

    private static bool TryGetHeaderIndices(IReadOnlyList<string> normalized,
                                            out int dateIndex,
                                            out int descriptionIndex,
                                            out int paymentsIndex,
                                            out int depositsIndex,
                                            out int bankIndex,
                                            out int feesIndex,
                                            out int balanceIndex)
    {
        dateIndex = descriptionIndex = paymentsIndex = depositsIndex = -1;
        bankIndex = feesIndex = balanceIndex = -1;

        if (normalized.Count == 7)
        {
            if (normalized[0] != "date" ||
                normalized[1] != "description" ||
                normalized[2] != "payments" ||
                normalized[3] != "deposits" ||
                normalized[4] != "bank" ||
                normalized[5] != "fees" ||
                normalized[6] != "balance")
            {
                return false;
            }
            dateIndex = 0;
            descriptionIndex = 1;
            paymentsIndex = 2;
            depositsIndex = 3;
            bankIndex = 4;
            feesIndex = 5;
            balanceIndex = 6;
            return true;
        }

        if (normalized.Count == 6)
        {
            if (normalized[0] != "date" ||
                normalized[1] != "description" ||
                normalized[2] != "payments" ||
                normalized[3] != "deposits" ||
                normalized[4] != "bankfees" ||
                normalized[5] != "balance")
            {
                return false;
            }
            dateIndex = 0;
            descriptionIndex = 1;
            paymentsIndex = 2;
            depositsIndex = 3;
            bankIndex = 4;
            feesIndex = 4;
            balanceIndex = 5;
            return true;
        }

        return false;
    }

    private static double Midpoint(double left, double right) => (left + right) / 2.0;

    private static string NormalizeLabel(string value) =>
        StatementText.CollapseWhitespace(value).ToLowerInvariant().Replace(" ", string.Empty);

    private static bool TryParsePeriodDate(string value, out DateOnly date)
    {
        // Only a printed four-digit year is accepted for prefill; two-digit
        // period dates would require a runtime pivot.
        return DateOnly.TryParseExact(value.Trim(), ["d MMM yyyy", "dd MMM yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private sealed record PdfWord(string Text,
                                  double Left,
                                  double Right,
                                  double Bottom,
                                  double Top)
    {
        public double CenterY => (Bottom + Top) / 2.0;

        public double CenterX => (Left + Right) / 2.0;
    }

    private sealed class PdfLine(PdfWord first)
    {
        private readonly List<PdfWord> _words = [first];

        public IReadOnlyList<PdfWord> Words => _words;

        public int SourceLine { get; set; }

        public double CenterY => _words.Average(word => word.CenterY);

        public string Text => string.Join(" ", _words.OrderBy(word => word.Left).Select(word => word.Text));

        public void Add(PdfWord word) => _words.Add(word);

        public void Sort() => _words.Sort((left, right) => left.Left.CompareTo(right.Left));

        public string TextIn(PdfHeader? header, int column)
        {
            if (header is null)
            {
                return Text;
            }
            var left = column == 0 ? 0 : header.Boundaries[column - 1];
            var right = column == 5 ? double.MaxValue : header.Boundaries[column];
            return string.Join(" ", _words
                .Where(word => word.CenterX >= left && word.CenterX < right)
                .OrderBy(word => word.Left)
                .Select(word => word.Text));
        }
    }

    private sealed record PdfHeader(double[] Boundaries, double DescriptionLeft, double CenterY)
    {
        public bool InColumn(PdfWord word, int column)
        {
            var left = column == 0 ? 0 : Boundaries[column - 1];
            var right = column == 5 ? double.MaxValue : Boundaries[column];
            return word.CenterX >= left && word.CenterX < right;
        }

        public bool IsCompatibleWith(PdfHeader other) =>
            Math.Abs(DescriptionLeft - other.DescriptionLeft) <= HeaderCompatibilityTolerance &&
            Boundaries.Zip(other.Boundaries,
                    (left, right) => Math.Abs(left - right) <= HeaderCompatibilityTolerance)
                .All(value => value);
    }

    private sealed record HeaderMatch(int LineIndex, PdfHeader Header);

    private enum BodyEventKind
    {
        Candidate,
        Total,
        BalanceOpening,
        BalanceClosing,
        Structural,
    }

    private sealed record BodyEvent(int Index, BodyEventKind Kind, PdfRowDraft? Draft);

    private sealed class PdfRowDraft
    {
        private readonly Match? _dateMatch;
        private readonly string _rawDate;
        private readonly DateOnly? _start;
        private readonly DateOnly? _end;
        private readonly int? _century;
        private readonly int _sourceLine;
        private readonly int _sourcePage;
        private readonly List<string> _continuations = [];
        private readonly string _description;
        private readonly string[] _flows;

        public PdfRowDraft(int sourceLine,
                           int sourcePage,
                           string rawDate,
                           Match? dateMatch,
                           string description,
                           string[] flows,
                           double lineCenterY,
                           DateOnly? start,
                           DateOnly? end,
                           int? century)
        {
            _sourceLine = sourceLine;
            _sourcePage = sourcePage;
            _rawDate = rawDate;
            _dateMatch = dateMatch;
            _description = description;
            _flows = flows;
            LastLineCenterY = lineCenterY;
            _start = start;
            _end = end;
            _century = century;
        }

        public int SourceRowNumber { get; set; }

        public double LastLineCenterY { get; private set; }

        public void AppendContinuation(string text, double lineCenterY)
        {
            _continuations.Add(text);
            LastLineCenterY = lineCenterY;
        }

        public ParsedStatementRow Build()
        {
            var validation = new List<string>();
            var warnings = new List<string>();
            var description = string.Join(" ", new[] { _description }.Concat(_continuations)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var (display, normalized, empty, tooLong) = StatementText.NormalizeDescription(description);
            if (empty)
            {
                warnings.Add(ValidationCodes.EmptyDescription);
            }
            if (tooLong)
            {
                validation.Add(ValidationCodes.DescriptionTooLong);
            }

            DateOnly? date = null;
            if (_dateMatch is null)
            {
                validation.Add(ValidationCodes.InvalidDate);
            }
            else
            {
                var day = int.Parse(_dateMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var month = ParseMonth(_dateMatch.Groups[2].Value);
                var yearText = _dateMatch.Groups[3].Value;
                if (month is null)
                {
                    validation.Add(ValidationCodes.InvalidDate);
                }
                else if (yearText.Length == 4)
                {
                    if (int.TryParse(yearText, out var year) && year is >= 1900 and <= 2099 &&
                        day >= 1 && day <= DateTime.DaysInMonth(year, month.Value))
                    {
                        date = new DateOnly(year, month.Value, day);
                    }
                    else
                    {
                        validation.Add(ValidationCodes.InvalidDate);
                    }
                }
                else
                {
                    if (!int.TryParse(yearText, out var year2))
                    {
                        validation.Add(ValidationCodes.InvalidDate);
                    }
                    else
                    {
                        var (resolved, multiple) = StatementDates.Resolve(
                            day, month.Value, year2, _start, _end, _century);
                        date = resolved;
                        if (resolved is null)
                        {
                            if (_start is null || _end is null)
                            {
                                warnings.Add(ValidationCodes.CenturyRequired);
                            }
                            else
                            {
                                validation.Add(multiple
                                    ? ValidationCodes.CenturyRequired
                                    : ValidationCodes.InvalidDate);
                            }
                        }
                    }
                }
            }

            if (date is not null)
            {
                if (_start is not null && _end is not null &&
                    !StatementDates.InPeriod(date.Value, _start.Value, _end.Value))
                {
                    validation.Add(ValidationCodes.DateOutsidePeriod);
                }
                if (StatementDates.IsFuture(date.Value))
                {
                    warnings.Add(ValidationCodes.FutureDate);
                }
            }

            var populated = _flows.Select((value, index) => (value, index))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.value))
                .ToList();
            long? cents = null;
            string? rawAmount = null;
            if (populated.Count == 0)
            {
                validation.Add(ValidationCodes.MissingAmount);
            }
            else if (populated.Count > 1)
            {
                validation.Add(ValidationCodes.MultipleAmounts);
                rawAmount = string.Join(" | ", populated.Select(entry => entry.value));
            }
            else
            {
                var (value, index) = populated[0];
                rawAmount = value;
                if (!StatementAmounts.TryParseMagnitude(value, ".", ",", out var magnitude,
                        out var positive, out var negative))
                {
                    validation.Add(ValidationCodes.InvalidAmount);
                }
                else
                {
                    var isOutflow = index is 0 or 2;
                    var expectedNegative = isOutflow;
                    if (magnitude == 0)
                    {
                        cents = 0;
                    }
                    else if (negative && !expectedNegative || positive && expectedNegative)
                    {
                        validation.Add(ValidationCodes.InvalidSign);
                    }
                    else
                    {
                        cents = expectedNegative ? -magnitude : magnitude;
                    }
                }
            }

            var raw = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["date"] = _rawDate,
                ["description"] = display,
                ["amount"] = rawAmount,
                ["flow_column"] = populated.Count == 1
                    ? _flowColumns[populated[0].index]
                    : null,
            };
            return new ParsedStatementRow(
                Guid.NewGuid(),
                _sourceLine,
                _sourcePage,
                SourceRowNumber,
                raw,
                date,
                display,
                normalized,
                cents,
                validation.Distinct(StringComparer.Ordinal).ToArray(),
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                null,
                null);
        }

        private static int? ParseMonth(string value) => value.ToLowerInvariant() switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => null,
        };
    }
}
