using argus.Server.Imports;
using UglyToad.PdfPig;

namespace argus.Server.Tests;

public sealed class StandardBankPdfProductionTests
{
    [Theory]
    [InlineData(ProductionCanary.Title)]
    [InlineData(ProductionCanary.Notice)]
    [InlineData(ProductionCanary.Footer)]
    [InlineData(ProductionCanary.AccountRight)]
    [InlineData(ProductionCanary.Available)]
    [InlineData(ProductionCanary.Payments)]
    public void SyntheticProductionFixedFieldsRejectResidue(ProductionCanary canary)
    {
        byte[] bytes = ProductionPdfFixture.Build(4, canary: canary);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void SyntheticProductionPeriodRequiresExplicitResolutionAndRejectsConflicts()
    {
        var noBasis = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4),
            null,
            null,
            null);
        Assert.Null(noBasis.ProposedStartDate);
        Assert.Null(noBasis.ProposedEndDate);

        var explicitCentury = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4),
            null,
            null,
            2000);
        Assert.Equal(new DateOnly(2025, 1, 1), explicitCentury.ProposedStartDate);
        Assert.Equal(new DateOnly(2025, 1, 31), explicitCentury.ProposedEndDate);

        var conflicting = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4, conflictingPeriod: true),
            null,
            null,
            2000);
        Assert.Null(conflicting.ProposedStartDate);
        Assert.Null(conflicting.ProposedEndDate);
    }

    [Theory]
    [InlineData(ProductionFlowVariant.CrossBoundary)]
    [InlineData(ProductionFlowVariant.Misaligned)]
    public void SyntheticProductionAmbiguousFlowRejects(ProductionFlowVariant variant)
    {
        byte[] bytes = ProductionPdfFixture.Build(4, flowVariant: variant);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void SyntheticProductionConflictingHeaderRejects()
    {
        byte[] bytes = ProductionPdfFixture.Build(4, conflictingHeader: true);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void SyntheticProductionPageLimitRejects()
    {
        byte[] bytes = ProductionPdfFixture.BuildManyPages(201);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
    }

    [Fact]
    public void SyntheticProductionCharacterLimitRejects()
    {
        byte[] bytes = ProductionPdfFixture.BuildOversizeCharacters(ImportLimits.MaxExtractedCharacters + 1);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
    }

    [Theory]
    [InlineData(4, 49)]
    [InlineData(18, 297)]
    public void SyntheticProductionProfileParses(int pageCount, int expectedRows)
    {
        byte[] bytes = ProductionPdfFixture.Build(pageCount);

        var result = StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000);

        Assert.Equal(expectedRows, result.Rows.Count);
        int missingDates = result.Rows.Count(row => row.TransactionDate is null);
        Assert.Equal(0, missingDates);
        int outsidePeriod = result.Rows.Count(row =>
            row.TransactionDate is null ||
            row.TransactionDate.Value < new DateOnly(2025, 1, 1) ||
            row.TransactionDate.Value > new DateOnly(2025, 1, 31));
        Assert.Equal(0, outsidePeriod);
        int rowsWithBalance = result.Rows.Count(row => row.RawValues.ContainsKey("balance"));
        Assert.Equal(0, rowsWithBalance);
    }

    [Theory]
    [InlineData(ProductionAssetVariant.Missing)]
    [InlineData(ProductionAssetVariant.DifferentBytes)]
    [InlineData(ProductionAssetVariant.Relocated)]
    [InlineData(ProductionAssetVariant.WrongFilter)]
    [InlineData(ProductionAssetVariant.WrongDimensions)]
    [InlineData(ProductionAssetVariant.Rotated)]
    [InlineData(ProductionAssetVariant.Masked)]
    public void SyntheticProductionAssetVariantsReject(ProductionAssetVariant variant)
    {
        byte[] bytes = ProductionPdfFixture.Build(4, asset: variant);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void SyntheticProductionPageWithoutTextRejects()
    {
        byte[] bytes = ProductionPdfFixture.Build(4, includeText: false);

        var exception = Assert.Throws<StatementRejectedException>(() => StandardBankPdfParser.Parse(
            bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void SyntheticProductionFlowVariantsRemainCandidates()
    {
        var zero = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4, "zero"),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000);
        Assert.Equal(49, zero.Rows.Count);
        Assert.Equal(49, zero.Rows.Count(row => row.AmountCents == 0));

        var invalid = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4, "invalid"),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000);
        Assert.Equal(49, invalid.Rows.Count);
        Assert.Equal(49, invalid.Rows.Count(row => row.ValidationCodes.Contains(ValidationCodes.InvalidAmount)));

        var multiple = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4, "multiple"),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000);
        Assert.Equal(49, multiple.Rows.Count);
        Assert.Equal(49, multiple.Rows.Count(row => row.ValidationCodes.Contains(ValidationCodes.MultipleAmounts)));

        var longDescription = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4, "long"),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000);
        Assert.Equal(49, longDescription.Rows.Count);
        Assert.Equal(49, longDescription.Rows.Count(row => row.Description.Contains("12345678901234567890")));
        Assert.Equal(0, longDescription.Rows.Count(row => row.AmountCents is null));

        var separateSign = StandardBankPdfParser.Parse(
            ProductionPdfFixture.Build(4, "separate-sign"),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            2000);
        Assert.Equal(49, separateSign.Rows.Count);
        Assert.Equal(49, separateSign.Rows.Count(row => row.AmountCents == -1000));
    }

    // Opt-in private evidence. CI does not set the folder, so the test returns
    // without reading a file. It asserts counts, page positions and booleans
    // only; no source value or filename is printed.
    [Fact]
    public async Task PrivateApprovedSamplesParseThroughPublishedChildWhenProvided()
    {
        string? folder = Environment.GetEnvironmentVariable("ARGUS_PRIVATE_PDF_DIR");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Dictionary<int, int> expectedRows = new()
        {
            [4] = 49,
            [18] = 297,
        };

        using PdfParserRunner runner = new(timeout: TimeSpan.FromSeconds(30));
        foreach (string file in Directory.GetFiles(folder, "*.pdf"))
        {
            byte[] bytes = File.ReadAllBytes(file);
            using PdfDocument document = PdfDocument.Open(bytes);
            int pages = document.NumberOfPages;

            var result = await runner.ParseAsync(bytes, new PdfChildRequestOptions(null, null, 2000, 30_000));

            Assert.Equal(expectedRows[pages], result.Rows.Count);
            int missingDates = result.Rows.Count(row => row.TransactionDate is null);
            Assert.Equal(0, missingDates);
            int rowsWithBalance = result.Rows.Count(row => row.RawValues.ContainsKey("balance"));
            Assert.Equal(0, rowsWithBalance);
        }
    }

    [Fact]
    public async Task SyntheticProductionProfileParsesThroughChild()
    {
        byte[] bytes = ProductionPdfFixture.Build(4);
        using PdfParserRunner runner = new(timeout: TimeSpan.FromSeconds(30));

        var result = await runner.ParseAsync(
            bytes,
            new PdfChildRequestOptions(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), 2000, 30_000));

        Assert.Equal(49, result.Rows.Count);
        int missingDates = result.Rows.Count(row => row.TransactionDate is null);
        Assert.Equal(0, missingDates);
        int rowsWithBalance = result.Rows.Count(row => row.RawValues.ContainsKey("balance"));
        Assert.Equal(0, rowsWithBalance);
    }

    // Opt-in private evidence. CI does not set the folder, so the test returns
    // without reading a file. It asserts counts, page positions and booleans
    // only; no source value or filename is printed.
    [Fact]
    public void PrivateApprovedSamplesParseWhenProvided()
    {
        string? folder = Environment.GetEnvironmentVariable("ARGUS_PRIVATE_PDF_DIR");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Dictionary<int, int> expectedRows = new()
        {
            [4] = 49,
            [18] = 297,
        };

        foreach (string file in Directory.GetFiles(folder, "*.pdf"))
        {
            byte[] bytes = File.ReadAllBytes(file);
            using PdfDocument document = PdfDocument.Open(bytes);
            int pages = document.NumberOfPages;

            var preview = StandardBankPdfParser.Parse(bytes, null, null, 2000);
            Assert.True(preview.ProposedStartDate is not null);
            Assert.True(preview.ProposedEndDate is not null);

            var result = StandardBankPdfParser.Parse(bytes, preview.ProposedStartDate, preview.ProposedEndDate, 2000);

            Assert.Equal(expectedRows[pages], result.Rows.Count);
            int missingDates = result.Rows.Count(row => row.TransactionDate is null);
            Assert.Equal(0, missingDates);
            int outsidePeriod = result.Rows.Count(row =>
                row.TransactionDate is null ||
                row.TransactionDate.Value < preview.ProposedStartDate!.Value ||
                row.TransactionDate.Value > preview.ProposedEndDate!.Value);
            Assert.Equal(0, outsidePeriod);
            int rowsWithBalance = result.Rows.Count(row => row.RawValues.ContainsKey("balance"));
            Assert.Equal(0, rowsWithBalance);
            Assert.Equal(pages, result.Rows.Max(row => row.SourcePage));
        }
    }
}
