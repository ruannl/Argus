using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace argus.Server.Imports;

public static partial class StandardBankPdfParser
{
    private const double ProductionPageWidth = 612;
    private const double ProductionPageHeight = 792;
    private const double ProductionPageTolerance = 1;
    private const double ProductionFirstHeaderCenterY = 535.29;
    private const double ProductionLaterHeaderCenterY = 605.35;
    private const double ProductionHeaderTolerance = 2.5;
    private const double ProductionDateRight = 88.5;
    private const double ProductionDescriptionLeft = 95;
    private const double ProductionDescriptionRight = 294;
    private const double ProductionPaymentsLeft = 296;
    private const double ProductionPaymentsRight = 364;
    private const double ProductionDepositsLeft = 367;
    private const double ProductionDepositsRight = 435;
    private const double ProductionBankFeesLeft = 438;
    private const double ProductionBankFeesRight = 506;
    private const double ProductionBalanceLeft = 509;
    private const double ProductionBalanceRight = 585.4;
    private const double ProductionPaymentsEdge = 358.6;
    private const double ProductionDepositsEdge = 429.4;
    private const double ProductionBankFeesEdge = 500.2;
    private const double ProductionBalanceEdge = 579.6;
    private const double ProductionLabelTolerance = 2;
    private const string ProductionAssetDigest = "4c734f111790a52ba855e81cce7e2481cb7e2d426d8dca0304674648531e3999";
    private const string ProductionSyntheticAssetDigest = "572a35e36a82caf743059c6d3363d7cbbec77061c7f445b6c8e566ee82c183cc";
    private const int ProductionAssetWidth = 656;
    private const int ProductionAssetHeight = 225;
    private const double ProductionAssetLeft = 18.43;
    private const double ProductionAssetRight = 175.82;
    private const double ProductionAssetBottom = 714.31;
    private const double ProductionAssetTop = 768.31;
    private const double ProductionAssetTolerance = 1;
    private const double ProductionCustomerCareY = 755.3;
    private const double ProductionWebsiteY = 743.9;
    private const double ProductionTitleY = 727.1;
    private const double ProductionLaterTitleY = 729.1;
    private const double ProductionLaterStatementValueY = 726.3;
    private const double ProductionFromY = 712.5;
    private const double ProductionIssuerSubtitleY = 705.8;
    private const double ProductionLaterIssuerSubtitleY = 706.6;
    private const double ProductionToY = 696.9;
    private const double ProductionIssueDateY = 683.4;
    private const double ProductionLaterIssueDateY = 684.2;
    private const double ProductionAddressHeadingY = 665.2;
    private const double ProductionSequenceY = 661.5;
    private const double ProductionLaterAccountY = 665;
    private const double ProductionLaterSequenceY = 662.3;
    private const double ProductionAccountY = 649.2;
    private const double ProductionHolderY = 633.6;
    private const double ProductionProductY = 618;
    private const double ProductionAddressLine4Y = 602;
    private const double ProductionAddressLine5Y = 586.3;
    private const double ProductionLeadInY = 558.6;
    private const double ProductionLaterLeadInY = 628.7;
    private const double ProductionMetadataTolerance = 2;
    private const double ProductionAddressSplitX = 300;
    private const double ProductionLeadInSplitX = 295;
    private const string ProductionCustomerCarePrefix = "Customer Care:";
    private const string ProductionWebsite = "Website: www.standardbank.co.za";
    private const string ProductionTitlePrefix = "STANDARD BANK";
    private const string ProductionTitleSuffix = "month statement";
    private const string ProductionFromPrefix = "From:";
    private const string ProductionToPrefix = "To:";
    private const string ProductionAccountNumberLabel = "Account number";
    private const string ProductionAccountHolderLabel = "Account holder";
    private const string ProductionProductNameLabel = "Product name";
    private const string ProductionAddressHeading = "Address:";
    private const string ProductionTransactionDetails = "Transaction details";
    private const string ProductionVerificationNotice = "Please verify all transactions reflected on this statement and notify any discrepancies to the bank as soon as possible.";
    private const string ProductionStatementSummary = "Statement Summary";
    private const string ProductionStatementOpeningBalance = "STATEMENT OPENING BALANCE";
    private const string ProductionPaymentsLabel = "Payments";
    private const string ProductionDepositsLabel = "Deposits";
    private const string ProductionClosingMessage = "Today's debits have not yet been paid";
    private const string ProductionBankingFooter = "We subscribe to the Code of Banking Association South Africa and, for unresolved disputes, support resolution through the Ombudsman for Banking Services";

    private static readonly Regex _productionPeriodDate = new(
        @"^\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _productionIssueDate = new(
        @"^\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _productionMoneyAtEnd = new(
        @"-?\s*R?\s*[\d,]+\.\d{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _productionTitleSuffix = new(
        @"^\d+\s+month statement$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _productionFullTitle = new(
        @"^STANDARD BANK \d+ month statement$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _productionAvailableBalance = new(
        @"^Available Balance:\s*R?\s*[\d,]+\.\d{2}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _productionLegalFooter = new(
        @"^The Standard Bank of South Africa Limited \(Reg\. No\. \d{4}/\d{6}/\d{2}\. Authorised financial service provider\. VAT Reg No\. \d{1,12}\.?\s+Registered credit provider \(NCRCP\d{1,4}\)\. Pg (\d+) of (\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _productionAccountValue = new(
        @"^[\d*\s-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsProductionDocument(PdfDocument document)
    {
        foreach (Page page in document.GetPages())
        {
            if (Math.Abs(page.Width - ProductionPageWidth) <= ProductionPageTolerance &&
                Math.Abs(page.Height - ProductionPageHeight) <= ProductionPageTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static ParsedStatement ParseProductionDocument(PdfDocument document,
                                                           DateOnly? start,
                                                           DateOnly? end,
                                                           int? century)
    {
        int pageCount = document.NumberOfPages;
        List<BodyEvent> events = [];
        List<PdfRowDraft> candidateDrafts = [];
        PdfRowDraft? current = null;
        int eventIndex = 0;
        int documentLine = 0;
        DateOnly? printedStart = null;
        DateOnly? printedEnd = null;
        string? printedFromText = null;
        string? printedToText = null;
        bool periodConflict = false;
        bool sawPostamble = false;
        bool sawCandidate = false;
        int postambleStep = -1;
        int totalCharacters = 0;
        foreach (Page page in document.GetPages())
        {
            ValidateProductionPage(page);
            PageFrame frame = GetPageFrame(page);
            List<IPdfImage> images = page.GetImages().ToList();
            ValidateProductionAsset(images, frame);
            List<PdfWord> words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new PdfWord(word.Text, word.BoundingBox.Left, word.BoundingBox.Right, word.BoundingBox.Bottom, word.BoundingBox.Top))
                .ToList();
            totalCharacters += words.Sum(word => word.Text.Length);
            if (totalCharacters > ImportLimits.MaxExtractedCharacters)
            {
                throw new StatementRejectedException(ParserReasons.ProcessingLimit);
            }

            foreach (PdfWord word in words)
            {
                ValidateWordFrame(word, frame);
            }

            List<PdfLine> lines = GroupLines(words);
            foreach (PdfLine line in lines)
            {
                line.SourceLine = ++documentLine;
            }

            bool firstPage = page.Number == 1;
            PdfLine? headerLine = FindProductionHeader(lines, firstPage);
            if (headerLine is null)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            PdfHeader header = BuildProductionHeader(headerLine);
            ValidateNoForeignProductionHeaders(lines, headerLine);
            List<PdfLine> aboveHeader = lines.Where(line => line.CenterY > headerLine.CenterY).ToList();
            ConsumeProductionMetadata(aboveHeader, firstPage, start, end, century, ref printedStart, ref printedEnd, ref printedFromText, ref printedToText, ref periodConflict);
            DateOnly? resolutionStart = start ?? printedStart;
            DateOnly? resolutionEnd = end ?? printedEnd;

            foreach (PdfLine line in lines.Where(line => line.CenterY < headerLine.CenterY).OrderByDescending(line => line.CenterY))
            {
                if (TryConsumeProductionFooter(line, page.Number, pageCount))
                {
                    current = null;
                    continue;
                }

                if (postambleStep >= 0)
                {
                    if (!TryConsumeProductionPostambleLine(line, ref postambleStep))
                    {
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }
                    continue;
                }

                if (IsProductionPostambleStart(line))
                {
                    if (page.Number != pageCount)
                    {
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }
                    sawPostamble = true;
                    postambleStep = 0;
                    current = null;
                    continue;
                }

                ValidateProductionColumns(line);
                string dateText = line.TextIn(header, 0);
                string descriptionText = line.TextIn(header, 1);
                string[] flowTexts = FlowTexts(line, header);
                string balanceText = line.TextIn(header, 5);
                if (dateText.Length == 0 &&
                    flowTexts.All(value => value.Length == 0) &&
                    string.Equals(Collapse(descriptionText), ProductionStatementOpeningBalance, StringComparison.OrdinalIgnoreCase) &&
                    _productionMoneyAtEnd.IsMatch(Collapse(balanceText)))
                {
                    if (sawCandidate || page.Number != 1)
                    {
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }
                    current = null;
                    continue;
                }

                if (dateText.Length > 0 || flowTexts.Any(value => value.Length > 0))
                {
                    var draft = AddCandidate(line, page.Number, dateText, descriptionText, flowTexts, resolutionStart, resolutionEnd, century, ref eventIndex, events, candidateDrafts);
                    current = draft;
                    sawCandidate = true;
                    continue;
                }

                if (descriptionText.Length > 0 && balanceText.Length == 0)
                {
                    if (current is null || !IsSupportedContinuation(line, header, current, crossPage: false))
                    {
                        throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
                    }
                    current.AppendContinuation(descriptionText, line.CenterY);
                    continue;
                }

                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            if (page.Number == pageCount && !sawPostamble)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }

        if (postambleStep >= 0 && postambleStep != 4)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        var finalEvents = events.Where(item => item.Kind == BodyEventKind.Candidate).OrderBy(item => item.Index).ToList();
        if (finalEvents.Count == 0)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }
        if (finalEvents.Count > ImportLimits.MaxDataRows)
        {
            throw new StatementRejectedException(ParserReasons.ProcessingLimit);
        }

        var parsedRows = new List<ParsedStatementRow>(finalEvents.Count);
        for (int index = 0; index < finalEvents.Count; index++)
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

    private static void ValidateProductionPage(Page page)
    {
        if (Math.Abs(page.Width - ProductionPageWidth) > ProductionPageTolerance ||
            Math.Abs(page.Height - ProductionPageHeight) > ProductionPageTolerance ||
            page.NumberOfImages != 1)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }
    }

    private static void ValidateProductionAsset(IReadOnlyList<IPdfImage> images, PageFrame frame)
    {
        if (images.Count != 1)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        IPdfImage image = images[0];
        PdfRectangle bounds = image.BoundingBox;
        if (image.WidthInSamples != ProductionAssetWidth ||
            image.HeightInSamples != ProductionAssetHeight ||
            image.IsImageMask ||
            image.MaskImage is not null ||
            Math.Abs(bounds.Left - ProductionAssetLeft) > ProductionAssetTolerance ||
            Math.Abs(bounds.Right - ProductionAssetRight) > ProductionAssetTolerance ||
            Math.Abs(bounds.Bottom - ProductionAssetBottom) > ProductionAssetTolerance ||
            Math.Abs(bounds.Top - ProductionAssetTop) > ProductionAssetTolerance ||
            bounds.Left < frame.Left ||
            bounds.Right > frame.Right ||
            bounds.Bottom < frame.Bottom ||
            bounds.Top > frame.Top ||
            (!HasFilter(image.ImageDictionary, "DCTDecode") && !HasFilter(image.ImageDictionary, "FlateDecode")))
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        if (image.BitsPerComponent != 8 ||
            image.ImageDictionary.Data.ContainsKey("Decode") ||
            image.ImageDictionary.Data.ContainsKey("ImageMask") ||
            image.ImageDictionary.Data.ContainsKey("Mask") ||
            image.ImageDictionary.Data.ContainsKey("SMask") ||
            !IsApprovedProductionColorSpace(image.ImageDictionary))
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        string digest = Convert.ToHexString(SHA256.HashData(image.RawBytes.ToArray())).ToLowerInvariant();
        bool realAsset = string.Equals(digest, ProductionAssetDigest, StringComparison.Ordinal) &&
                         HasFilter(image.ImageDictionary, "DCTDecode");
        bool syntheticAsset = string.Equals(digest, ProductionSyntheticAssetDigest, StringComparison.Ordinal) &&
                              HasFilter(image.ImageDictionary, "FlateDecode");
        if (!realAsset && !syntheticAsset)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }
    }

    private static bool HasFilter(DictionaryToken dictionary, string filter) =>
        dictionary.Data.TryGetValue("Filter", out IToken? value) &&
        value is NameToken name &&
        string.Equals(name.Data, filter, StringComparison.Ordinal);

    private static bool IsApprovedProductionColorSpace(DictionaryToken dictionary)
    {
        if (!dictionary.Data.TryGetValue("ColorSpace", out IToken? token))
        {
            return false;
        }

        if (token is NameToken name)
        {
            return name.Data is "DeviceRGB" or "DeviceGray";
        }

        if (token is ArrayToken array &&
            array.Data.Count >= 2 &&
            array.Data[0] is NameToken iccBased &&
            string.Equals(iccBased.Data, "ICCBased", StringComparison.Ordinal) &&
            array.Data[1] is StreamToken profile &&
            profile.StreamDictionary.Data.TryGetValue("N", out IToken? components) &&
            components is NumericToken number)
        {
            return number.Long is 1 or 3;
        }

        return false;
    }

    private static PdfLine? FindProductionHeader(IReadOnlyList<PdfLine> lines, bool firstPage)
    {
        double expectedY = firstPage ? ProductionFirstHeaderCenterY : ProductionLaterHeaderCenterY;
        PdfLine? match = null;
        foreach (PdfLine line in lines)
        {
            if (Math.Abs(line.CenterY - expectedY) > ProductionHeaderTolerance)
            {
                continue;
            }

            List<PdfWord> words = line.Words.OrderBy(word => word.Left).ToList();
            List<string> normalized = words.Select(word => NormalizeLabel(word.Text)).ToList();
            if (!TryFindHeaderCore(normalized, out int coreStart, out int coreLength, out int dateIndex, out int descriptionIndex, out int paymentsIndex, out int depositsIndex, out int bankIndex, out int feesIndex, out int balanceIndex))
            {
                continue;
            }
            if (coreStart != 0 || coreStart + coreLength != normalized.Count)
            {
                continue;
            }
            if (!Near(words[dateIndex].Left, 38.3, ProductionLabelTolerance) ||
                !Near(words[descriptionIndex].Left, ProductionDescriptionLeft, ProductionLabelTolerance) ||
                !Near(words[paymentsIndex].Right, ProductionPaymentsEdge, ProductionLabelTolerance) ||
                !Near(words[depositsIndex].Right, ProductionDepositsEdge, ProductionLabelTolerance) ||
                !Near(words[feesIndex].Right, ProductionBankFeesEdge, ProductionLabelTolerance) ||
                !Near(words[balanceIndex].Right, ProductionBalanceEdge, ProductionLabelTolerance))
            {
                continue;
            }

            if (match is not null)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
            match = line;
        }

        return match;
    }

    private static void ValidateNoForeignProductionHeaders(IReadOnlyList<PdfLine> lines, PdfLine headerLine)
    {
        foreach (PdfLine line in lines)
        {
            if (ReferenceEquals(line, headerLine))
            {
                continue;
            }

            List<string> normalized = line.Words
                .OrderBy(word => word.Left)
                .Select(word => NormalizeLabel(word.Text))
                .ToList();
            if (TryFindHeaderCore(normalized, out _, out _, out _, out _, out _, out _, out _, out _, out _))
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }
    }

    private static PdfHeader BuildProductionHeader(PdfLine headerLine)
    {
        double[] boundaries =
        [
            (ProductionDateRight + ProductionDescriptionLeft) / 2,
            (ProductionDescriptionRight + ProductionPaymentsLeft) / 2,
            (ProductionPaymentsRight + ProductionDepositsLeft) / 2,
            (ProductionDepositsRight + ProductionBankFeesLeft) / 2,
            (ProductionBankFeesRight + ProductionBalanceLeft) / 2,
        ];

        return new PdfHeader(boundaries, ProductionDescriptionLeft, headerLine.CenterY);
    }

    private static void ConsumeProductionMetadata(IReadOnlyList<PdfLine> lines,
                                                  bool firstPage,
                                                  DateOnly? start,
                                                  DateOnly? end,
                                                  int? century,
                                                  ref DateOnly? printedStart,
                                                  ref DateOnly? printedEnd,
                                                  ref string? printedFromText,
                                                  ref string? printedToText,
                                                  ref bool periodConflict)
    {
        List<PdfLine> ordered = lines.OrderByDescending(line => line.CenterY).ToList();
        int index = 0;
        if (firstPage)
        {
            ConsumeAt(ordered, ref index, ProductionCustomerCareY, IsCustomerCare);
            ConsumeAt(ordered, ref index, ProductionWebsiteY, line => IsProductionText(line, ProductionWebsite));
            ConsumeAt(ordered, ref index, ProductionTitleY, IsProductionFullTitle);
            ConsumeAt(ordered, ref index, ProductionFromY, line => IsProductionPrefixedDate(line, ProductionFromPrefix));
            ConsumeAt(ordered, ref index, ProductionIssuerSubtitleY, line => IsBoundedText(line.Text, 128));
            ConsumeAt(ordered, ref index, ProductionToY, line => IsProductionPrefixedDate(line, ProductionToPrefix));
            ConsumeAt(ordered, ref index, ProductionIssueDateY, line => _productionIssueDate.IsMatch(Collapse(line.Text)));
            ConsumeAt(ordered, ref index, ProductionAddressHeadingY, line => IsProductionText(line, ProductionAddressHeading));
            ConsumeAt(ordered, ref index, ProductionSequenceY, line => IsInteger(line.Text));
            ConsumeAt(ordered, ref index, ProductionAccountY, line => IsAccountAndAddress(line, ProductionAccountNumberLabel));
            ConsumeAt(ordered, ref index, ProductionHolderY, line => IsAccountAndAddress(line, ProductionAccountHolderLabel));
            ConsumeAt(ordered, ref index, ProductionProductY, line => IsAccountAndAddress(line, ProductionProductNameLabel));
            ConsumeAt(ordered, ref index, ProductionAddressLine4Y, IsAddressOnly);
            ConsumeAt(ordered, ref index, ProductionAddressLine5Y, IsAddressOnly);
            ConsumeAt(ordered, ref index, ProductionLeadInY, IsLeadIn);
        }
        else
        {
            ConsumeAt(ordered, ref index, ProductionCustomerCareY, IsCustomerCare);
            ConsumeAt(ordered, ref index, ProductionWebsiteY, line => IsProductionText(line, ProductionWebsite));
            ConsumeAt(ordered, ref index, ProductionLaterTitleY, IsProductionTitle);
            ConsumeAt(ordered, ref index, ProductionLaterStatementValueY, line => _productionTitleSuffix.IsMatch(Collapse(line.Text)));
            ConsumeAt(ordered, ref index, ProductionFromY, line => IsProductionPrefixedDate(line, ProductionFromPrefix));
            ConsumeAt(ordered, ref index, ProductionLaterIssuerSubtitleY, line => IsBoundedText(line.Text, 128));
            ConsumeAt(ordered, ref index, ProductionToY, line => IsProductionPrefixedDate(line, ProductionToPrefix));
            ConsumeAt(ordered, ref index, ProductionLaterIssueDateY, line => _productionIssueDate.IsMatch(Collapse(line.Text)));
            ConsumeAt(ordered, ref index, ProductionLaterAccountY, line => IsAccountOnly(line, ProductionAccountNumberLabel));
            ConsumeAt(ordered, ref index, ProductionLaterSequenceY, line => IsInteger(line.Text));
            ConsumeAt(ordered, ref index, ProductionLaterLeadInY, IsLeadIn);
        }

        if (index != ordered.Count)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }

        ParseProductionPeriod(ordered, start, end, century, ref printedStart, ref printedEnd, ref printedFromText, ref printedToText, ref periodConflict);
    }

    private static void ConsumeAt(IReadOnlyList<PdfLine> lines, ref int index, double expectedY, Func<PdfLine, bool> validate)
    {
        if (index >= lines.Count ||
            Math.Abs(lines[index].CenterY - expectedY) > ProductionMetadataTolerance ||
            !validate(lines[index]))
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
        }
        index++;
    }

    private static void ParseProductionPeriod(IReadOnlyList<PdfLine> lines,
                                              DateOnly? start,
                                              DateOnly? end,
                                              int? century,
                                              ref DateOnly? printedStart,
                                              ref DateOnly? printedEnd,
                                              ref string? printedFromText,
                                              ref string? printedToText,
                                              ref bool periodConflict)
    {
        PdfLine? fromLine = lines.FirstOrDefault(line => IsProductionPrefixedDate(line, ProductionFromPrefix));
        PdfLine? toLine = lines.FirstOrDefault(line => IsProductionPrefixedDate(line, ProductionToPrefix));
        if (fromLine is null || toLine is null)
        {
            return;
        }

        string fromText = Collapse(TextAfterPrefix(fromLine, ProductionFromPrefix));
        string toText = Collapse(TextAfterPrefix(toLine, ProductionToPrefix));
        if (printedFromText is not null &&
            (!string.Equals(printedFromText, fromText, StringComparison.Ordinal) ||
             !string.Equals(printedToText, toText, StringComparison.Ordinal)))
        {
            periodConflict = true;
            printedStart = null;
            printedEnd = null;
            return;
        }

        printedFromText ??= fromText;
        printedToText ??= toText;
        if (periodConflict)
        {
            return;
        }

        if (!TryParseProductionPeriodDate(fromText, start, end, century, out DateOnly resolvedStart) ||
            !TryParseProductionPeriodDate(toText, start, end, century, out DateOnly resolvedEnd) ||
            resolvedEnd < resolvedStart)
        {
            return;
        }

        printedStart = resolvedStart;
        printedEnd = resolvedEnd;
    }

    private static bool TryParseProductionPeriodDate(string text,
                                                     DateOnly? start,
                                                     DateOnly? end,
                                                     int? century,
                                                     out DateOnly date)
    {
        date = default;
        string[] parts = Collapse(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int day))
        {
            return false;
        }

        int? month = ParseProductionMonth(parts[1]);
        if (month is null || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int year))
        {
            return false;
        }

        if (parts[2].Length == 2)
        {
            if (century is not null)
            {
                year += century.Value;
            }
            else if (start is not null && end is not null)
            {
                var (resolved, _) = StatementDates.Resolve(day, month.Value, year, start, end, null);
                if (resolved is null)
                {
                    return false;
                }
                date = resolved.Value;
                return true;
            }
            else
            {
                return false;
            }
        }

        if (year is < 1900 or > 2099 || day < 1 || day > DateTime.DaysInMonth(year, month.Value))
        {
            return false;
        }

        date = new DateOnly(year, month.Value, day);
        return true;
    }

    private static bool IsCustomerCare(PdfLine line)
    {
        string text = Collapse(line.Text);
        return text.StartsWith(ProductionCustomerCarePrefix, StringComparison.OrdinalIgnoreCase) &&
               text[ProductionCustomerCarePrefix.Length..].Trim().All(character => char.IsDigit(character) || character == ' ');
    }

    private static bool IsProductionTitle(PdfLine line) =>
        string.Equals(Collapse(line.Text), ProductionTitlePrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsProductionFullTitle(PdfLine line) =>
        _productionFullTitle.IsMatch(Collapse(line.Text));

    private static bool IsProductionPrefixedDate(PdfLine line, string prefix)
    {
        string text = Collapse(line.Text);
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               _productionPeriodDate.IsMatch(text[prefix.Length..].Trim());
    }

    private static bool IsProductionText(PdfLine line, string expected) =>
        string.Equals(Collapse(line.Text), expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsInteger(string text) =>
        int.TryParse(Collapse(text), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsBoundedText(string text, int maximumLength)
    {
        string collapsed = Collapse(text);
        return collapsed.Length > 0 && collapsed.Length <= maximumLength;
    }

    private static bool IsAccountAndAddress(PdfLine line, string label)
    {
        string left = TextBefore(line, ProductionAddressSplitX);
        string right = TextAfter(line, ProductionAddressSplitX);
        if (!left.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string value = left[label.Length..].Trim().TrimStart(':').Trim();
        bool validValue = label == ProductionAccountNumberLabel
            ? _productionAccountValue.IsMatch(value)
            : value.Length is > 0 and <= 128;
        return validValue && IsBoundedText(right, 80);
    }

    private static bool IsAccountOnly(PdfLine line, string label)
    {
        string left = TextBefore(line, ProductionAddressSplitX);
        if (!left.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string value = left[label.Length..].Trim().TrimStart(':').Trim();
        if (value.Length == 0 || !_productionAccountValue.IsMatch(value))
        {
            return false;
        }

        string right = TextAfter(line, ProductionAddressSplitX);
        return right.Length == 0 || (right.Length <= 16 && _productionAccountValue.IsMatch(right));
    }

    private static bool IsAddressOnly(PdfLine line)
    {
        string left = TextBefore(line, ProductionAddressSplitX);
        string right = TextAfter(line, ProductionAddressSplitX);
        return left.Length == 0 && IsBoundedText(right, 80);
    }

    private static bool IsLeadIn(PdfLine line)
    {
        string left = TextBefore(line, ProductionLeadInSplitX);
        string right = TextAfter(line, ProductionLeadInSplitX);
        return string.Equals(left, ProductionTransactionDetails, StringComparison.OrdinalIgnoreCase) &&
               _productionAvailableBalance.IsMatch(right);
    }

    private static void ValidateProductionColumns(PdfLine line)
    {
        foreach (PdfWord word in line.Words)
        {
            int column = ProductionColumnOf(word.CenterX);
            if (column < 0)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            (double left, double right) = column switch
            {
                0 => (0, ProductionDescriptionLeft),
                1 => (ProductionDateRight, ProductionPaymentsLeft),
                2 => (ProductionDescriptionRight, ProductionDepositsLeft),
                3 => (ProductionPaymentsRight, ProductionBankFeesLeft),
                4 => (ProductionDepositsRight, ProductionBalanceLeft),
                _ => (ProductionBankFeesRight, ProductionBalanceRight),
            };
            if (word.Left < left - 1.0 || word.Right > right + 1.0)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }

        ValidateFlowAlignment(line);
    }

    private static void ValidateFlowAlignment(PdfLine line)
    {
        foreach ((double edge, double left, double right) in new[]
                 {
                     (ProductionPaymentsEdge, ProductionPaymentsLeft, ProductionPaymentsRight),
                     (ProductionDepositsEdge, ProductionDepositsLeft, ProductionDepositsRight),
                     (ProductionBankFeesEdge, ProductionBankFeesLeft, ProductionBankFeesRight),
                 })
        {
            List<PdfWord> words = line.Words
                .Where(word => word.CenterX >= left && word.CenterX < right)
                .OrderBy(word => word.Left)
                .ToList();
            if (words.Count == 0)
            {
                continue;
            }

            string text = Collapse(string.Join(" ", words.Select(word => word.Text)));
            if (!_productionMoneyAtEnd.IsMatch(text))
            {
                // Malformed flow text stays a visible invalid row; alignment
                // applies only to a well-formed money run.
                continue;
            }

            PdfWord last = words.OrderBy(word => word.Right).Last();
            if (Math.Abs(last.Right - edge) > 6.0)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }
        }
    }

    private static int ProductionColumnOf(double x) =>
        x >= 0 && x < ProductionDateRight ? 0 :
        x >= ProductionDescriptionLeft && x < ProductionDescriptionRight ? 1 :
        x >= ProductionPaymentsLeft && x < ProductionPaymentsRight ? 2 :
        x >= ProductionDepositsLeft && x < ProductionDepositsRight ? 3 :
        x >= ProductionBankFeesLeft && x < ProductionBankFeesRight ? 4 :
        x >= ProductionBalanceLeft && x <= ProductionBalanceRight ? 5 : -1;

    private static bool IsProductionPostambleStart(PdfLine line) =>
        string.Equals(Collapse(line.Text), ProductionVerificationNotice, StringComparison.OrdinalIgnoreCase);

    private static bool TryConsumeProductionPostambleLine(PdfLine line, ref int step)
    {
        string text = Collapse(line.Text);
        bool matched = step switch
        {
            0 => string.Equals(text, ProductionStatementSummary, StringComparison.OrdinalIgnoreCase),
            1 => IsProductionTotalLine(text, ProductionPaymentsLabel),
            2 => IsProductionTotalLine(text, ProductionDepositsLabel),
            3 => string.Equals(NormalizeApostrophes(text), ProductionClosingMessage, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        if (matched)
        {
            step++;
        }

        return matched;
    }

    private static bool IsProductionTotalLine(string text, string label) =>
        Regex.IsMatch(
            text,
            $@"^{Regex.Escape(label)}\s*-?\s*R?\s*[\d,]+\.\d{{2}}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool TryConsumeProductionFooter(PdfLine line, int pageNumber, int pageCount)
    {
        if (line.CenterY > 60)
        {
            return false;
        }

        string text = Collapse(line.Text);
        Match match = _productionLegalFooter.Match(text);
        if (match.Success)
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int total) ||
                number != pageNumber ||
                total != pageCount)
            {
                throw new StatementRejectedException(ParserReasons.UnsupportedLayout);
            }

            return true;
        }

        return string.Equals(text, ProductionBankingFooter, StringComparison.OrdinalIgnoreCase);
    }

    private static string TextBefore(PdfLine line, double x) =>
        string.Join(" ", line.Words.Where(word => word.CenterX < x).OrderBy(word => word.Left).Select(word => word.Text));

    private static string TextAfter(PdfLine line, double x) =>
        string.Join(" ", line.Words.Where(word => word.CenterX >= x).OrderBy(word => word.Left).Select(word => word.Text));

    private static string TextAfterPrefix(PdfLine line, string prefix)
    {
        string text = Collapse(line.Text);
        return text[prefix.Length..].Trim();
    }

    private static string Collapse(string text) => StatementText.CollapseWhitespace(text);

    private static string NormalizeApostrophes(string text) =>
        text.Replace('\u2019', '\'').Replace('\u2018', '\'');

    private static int? ParseProductionMonth(string value) => value.ToLowerInvariant() switch
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

    private static bool Near(double value, double expected, double tolerance) => Math.Abs(value - expected) <= tolerance;
}
