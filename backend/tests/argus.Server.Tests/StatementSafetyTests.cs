using System.IO.Compression;
using System.Text;
using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class StatementSafetyTests
{
    [Fact]
    public void FnbMalformedFieldCountIsAnInvalidRowWithoutExtraFields()
    {
        const string csv = "one\ntwo\nthree\nfour\nfive\nsix\nDate,Amount,Balance,Description\n" + "2024/01/02,-10.00,0.00,Good row\n" + "2024/01/03,-11.00,0.00,Bad,row,extra\n";
        var result = CsvStatementParsers.ParseFnb(Encoding.UTF8.GetBytes(csv), null, null);

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Rows[0].IsValid);
        Assert.Contains("invalid_field_count", result.Rows[1].ValidationCodes);
        Assert.False(result.Rows[1].RawValues.ContainsKey("balance"));
        Assert.False(result.Rows[1].RawValues.ContainsKey("field_5"));
        Assert.Equal("4", result.Rows[1].RawValues["expected_fields"]);
        Assert.Equal("6", result.Rows[1].RawValues["actual_fields"]);
    }

    [Fact]
    public void FnbPreambleAndBalanceNeverAppearInParsedOutput()
    {
        var fixture = StatementFixtureBuilder.BuildFnbCsv(3, chequeAccount: true);
        var result = CsvStatementParsers.ParseFnb(fixture.Bytes, null, null);
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.DoesNotContain("Synthetic FNB export", serialized);
        Assert.DoesNotContain("SYNTHETIC-0000", serialized);
        Assert.DoesNotContain("Do not use with real data", serialized);
        Assert.All(result.Rows, row => Assert.False(row.RawValues.ContainsKey("balance")));
    }

    [Fact]
    public void ZipWithSymlinkEntryIsRejected()
    {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive archive = new System.IO.Compression.ZipArchive(
                   output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("statement.csv");
            entry.ExternalAttributes = unchecked((int)0xA000 << 16);
            using var stream = entry.Open();
            stream.Write(System.Text.Encoding.UTF8.GetBytes("Date,Amount,Balance,Description\n"));
        }

        AssertRejected(output.ToArray(), ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void FnbRowLimitPlusOneIsRejected()
    {
        StringBuilder builder = new System.Text.StringBuilder();
        builder.Append("one\ntwo\nthree\nfour\nfive\nsix\n");
        builder.Append("Date,Amount,Balance,Description\n");
        for (var index = 0; index <= ImportLimits.MaxDataRows; index++)
        {
            builder.Append("2024/01/02,-10.00,0.00,Row ").Append(index).Append('\n');
        }

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseFnb(System.Text.Encoding.UTF8.GetBytes(builder.ToString()), null, null));
        Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
    }

    [Fact]
    public void ZipWithNoEntriesIsRejected()
    {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            _ = archive;
        }

        AssertRejected(output.ToArray(), ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void ZipWithNestedPathIsRejected()
    {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("nested/statement.csv").Open();
            stream.Write(Encoding.UTF8.GetBytes("Date,Amount,Balance,Description\n"));
        }

        AssertRejected(output.ToArray(), ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void ZipWithTraversalPathIsRejected()
    {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("../statement.csv").Open();
            stream.Write(Encoding.UTF8.GetBytes("Date,Amount,Balance,Description\n"));
        }

        AssertRejected(output.ToArray(), ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void ZipWithNonCsvEntryIsRejected()
    {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("statement.txt").Open();
            stream.Write(Encoding.UTF8.GetBytes("Date,Amount,Balance,Description\n"));
        }

        AssertRejected(output.ToArray(), ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void ZipWithLyingExpansionIsRejected()
    {
        using MemoryStream output = new MemoryStream();
        using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("statement.csv").Open();
            var chunk = new byte[64 * 1024];
            var written = 0;
            while (written <= 10_000_000)
            {
                stream.Write(chunk);
                written += chunk.Length;
            }
        }

        AssertRejected(output.ToArray(), ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void MalformedZipIsRejected()
    {
        AssertRejected([0x50, 0x4B, 0x03, 0x04, 0x00], ParserReasons.UnsafeArchive);
    }

    [Fact]
    public void EncryptedPdfIsRejectedBeforeExtraction()
    {
        var pdf = StatementFixtureBuilder.BuildHandAuthoredSmallPdf();
        var text = Encoding.ASCII.GetString(pdf);
        var encrypted = text.Replace("trailer\n<< /Size", "trailer\n<< /Encrypt 99 0 R /Size", StringComparison.Ordinal);
        Assert.NotEqual(text, encrypted);

        AssertRejected(Encoding.ASCII.GetBytes(encrypted), ParserReasons.EncryptedPdf);
    }

    [Fact]
    public void ImageOnlyPageAlongsideAValidTableRejectsAsTextPdfRequired()
    {
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), secondPage: null, secondPageIsImage: true);

        AssertRejected(pdf, ParserReasons.TextPdfRequired);
    }

    [Theory]
    [InlineData("2025/01/02")]
    [InlineData("02 Jan 25")]
    public void HeaderlessTransactionPageAlongsideAValidTableRejectsLayout(string date)
    {
        List<PdfText> second = new List<PdfText>
        {
            new(40, 760, date),
            new(100, 760, "Hand written continuation"),
            new(300, 760, "-20.00"),
            new(530, 760, "30.00"),
        };
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), second);

        AssertRejected(pdf, ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void DescriptionAndFlowWithoutDateRemainsAnInvalidCandidate()
    {
        List<PdfText> texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "No date payment"),
            new(300, 706, "-20.00"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), null, null, null);

        Assert.Equal(4, result.Rows.Count);
        var candidate = Assert.Single(result.Rows, row => row.ValidationCodes.Contains(ValidationCodes.InvalidDate) && row.RawValues["description"] == "No date payment");
        Assert.Null(candidate.TransactionDate);
    }

    [Fact]
    public void ExplicitSummaryRowWithAFlowIsNotATransactionCandidate()
    {
        List<PdfText> texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), null, null, null);

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void HeaderWithALargeImageBodyRejectsAsTextPdfRequired()
    {
        List<PdfText> header = new List<PdfText>
        {
            new(40, 780, "Date"),
            new(100, 780, "Description"),
            new(300, 780, "Payments"),
            new(400, 780, "Deposits"),
            new(470, 780, "Bank"),
            new(500, 780, "fees"),
            new(530, 780, "Balance"),
        };
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), header, secondPageHasImage: true);

        AssertRejected(pdf, ParserReasons.TextPdfRequired);
    }

    [Fact]
    public void HeaderlessPageWithMalformedDateAndMoneyRejectsUnlessClearlyCoverContent()
    {
        List<PdfText> page = new List<PdfText>
        {
            new(40, 760, "BAD-DATE"),
            new(100, 760, "Bank charge"),
            new(300, 760, "-20.00"),
        };
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), page);

        AssertRejected(pdf, ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void HeaderlessPageWithDateAndIntegerAmountRejects()
    {
        List<PdfText> page = new List<PdfText>
        {
            new(40, 760, "02 Jan 25"),
            new(100, 760, "Bank charge"),
            new(300, 760, "20"),
        };
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), page);

        AssertRejected(pdf, ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void SummaryVocabularyInADescriptionDoesNotDropTheMissingDateCandidate()
    {
        List<PdfText> texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Summary Books purchase"),
            new(300, 706, "-20.00"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), null, null, null);

        Assert.Equal(4, result.Rows.Count);
        var candidate = Assert.Single(result.Rows, row => row.ValidationCodes.Contains(ValidationCodes.InvalidDate) && row.RawValues["description"] == "Summary Books purchase");
        Assert.Null(candidate.TransactionDate);
    }

    [Theory]
    [InlineData("encrypted-empty-password.pdf")]
    [InlineData("encrypted-user-password.pdf")]
    public void GenuinelyEncryptedPdfIsRejectedBeforeExtraction(string name)
    {
        // Fixtures are synthetic PDFs written by pypdf 5.1.0: one RC4-128
        // document with an empty user password, and one AES-128 document with
        // a non-empty user password. Neither contains customer data.
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Statements", name);
        Assert.True(File.Exists(path), $"missing fixture {path}");

        AssertRejected(File.ReadAllBytes(path), ParserReasons.EncryptedPdf);
    }

    [Fact]
    public void TextlessPdfWithoutImagesIsARejectedLayout()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage([new PdfText(40, 800, "Synthetic Bank")]);
        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void ImageOnlyPdfRequiresATextPdf()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        AssertRejected(pdf.BuildImageOnly(), ParserReasons.TextPdfRequired);
    }

    [Fact]
    public void TooManyPdfPagesIsAProcessingLimit()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        for (var index = 0; index < ImportLimits.MaxPdfPages + 1; index++)
        {
            pdf.AddPage([]);
        }

        AssertRejected(pdf.Build(), ParserReasons.ProcessingLimit);
    }

    [Fact]
    public void UnknownPdfLayoutIsRejected()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage([new PdfText(40, 800, "This is not a statement")]);
        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Theory]
    [InlineData("-1,000.00")]
    [InlineData("-1000.00")]
    [InlineData("20")]
    [InlineData("0.00")]
    [InlineData("garbage")]
    [InlineData("")]
    public void HeaderlessPeriodPageWithAnyAmountOrNoDateRejects(string amount)
    {
        List<PdfText> second = new List<PdfText>
        {
            new(40, 780, "Statement period: 01 Jan 2025 to 31 Jan 2025"),
            new(40, 760, "BAD-DATE"),
            new(100, 760, "Bank charge"),
        };
        if (amount.Length > 0)
        {
            second.Add(new PdfText(300, 760, amount));
        }
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), second);

        AssertRejected(pdf, ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void HeaderlessPeriodPageWithArbitraryTextRejects()
    {
        List<PdfText> second = new List<PdfText>
        {
            new(40, 780, "Statement period: 01 Jan 2025 to 31 Jan 2025"),
            new(100, 760, "Just some text"),
        };
        var pdf = StatementFixtureBuilder.BuildTwoPagePdf(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), second);

        AssertRejected(pdf, ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void HeaderlessSummaryHeadingDoesNotAuthorizeOtherText()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(40, 780, "Summary"), new PdfText(100, 760, "Total payments 1,234.56"),]);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void HeaderlessMetadataAndUnknownTextReject()
    {
        MinimalPdfBuilder metadata = new MinimalPdfBuilder();
        metadata.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(40, 780, "Account: 123456789"),]);
        AssertRejected(metadata.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder unknown = new MinimalPdfBuilder();
        unknown.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(100, 780, "Just some text"),]);
        AssertRejected(unknown.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void OrphanAndPoorlySpacedContinuationsReject()
    {
        MinimalPdfBuilder orphan = new MinimalPdfBuilder();
        List<PdfText> orphanTexts = new List<PdfText>();
        AddPdfHeader(orphanTexts);
        orphanTexts.Add(new PdfText(100, 760, "No preceding row"));
        orphan.AddPage(orphanTexts);
        AssertRejected(orphan.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> far = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 700, "Too far from the row"),
        };
        MinimalPdfBuilder farPdf = new MinimalPdfBuilder();
        farPdf.AddPage(far);
        AssertRejected(farPdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void InterveningCoverClosesTheContinuationSequence()
    {
        List<PdfText> first = new List<PdfText>();
        AddPdfHeader(first);
        first.Add(new PdfText(40, 760, "01 Feb 25"));
        first.Add(new PdfText(100, 760, "First row"));
        first.Add(new PdfText(300, 760, "10.00"));

        List<PdfText> third = new List<PdfText>();
        AddPdfHeader(third);
        third.Add(new PdfText(100, 770, "continued after a cover"));
        third.Add(new PdfText(40, 752, "02 Feb 25"));
        third.Add(new PdfText(100, 752, "Second row"));
        third.Add(new PdfText(300, 752, "20.00"));

        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage([new PdfText(40, 780, "Statement period: 01 Feb 2025 to 28 Feb 2025")]);
        pdf.AddPage(third);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void BlankOrSummaryPageClosesTheContinuationSequence()
    {
        static (byte[] Pdf, string Reason) Build(IReadOnlyList<PdfText>? intervening)
        {
            List<PdfText> first = new List<PdfText>();
            AddPdfHeader(first);
            first.Add(new PdfText(40, 760, "01 Feb 25"));
            first.Add(new PdfText(100, 760, "First row"));
            first.Add(new PdfText(300, 760, "10.00"));

            List<PdfText> third = new List<PdfText>();
            AddPdfHeader(third);
            third.Add(new PdfText(100, 770, "continued after a page break"));
            third.Add(new PdfText(40, 752, "02 Feb 25"));
            third.Add(new PdfText(100, 752, "Second row"));
            third.Add(new PdfText(300, 752, "20.00"));

            MinimalPdfBuilder pdf = new MinimalPdfBuilder();
            pdf.AddPage(first);
            pdf.AddPage(intervening ?? []);
            pdf.AddPage(third);

            return (pdf.Build(), ParserReasons.UnsupportedLayout);
        }

        AssertRejected(Build([new PdfText(40, 780, "Summary")]).Pdf, ParserReasons.UnsupportedLayout);
        AssertRejected(Build(null).Pdf, ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void PdfRowLimitPlusOneIsAProcessingLimit()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        var row = 0;
        while (row <= ImportLimits.MaxDataRows)
        {
            List<PdfText> texts = new List<PdfText>();
            AddPdfHeader(texts);
            for (var index = 0; index < 250 && row <= ImportLimits.MaxDataRows; index++, row++)
            {
                var y = 780 - index * 3;
                texts.Add(new PdfText(40, y, "01 Feb 25"));
                texts.Add(new PdfText(100, y, $"Row {row:D5}"));
                texts.Add(new PdfText(300, y, "10.00"));
            }
            pdf.AddPage(texts);
        }

        AssertRejected(pdf.Build(), ParserReasons.ProcessingLimit);
    }

    [Fact]
    public void IncompatibleContinuationHeaderRejects()
    {
        List<PdfText> first = new List<PdfText>();
        AddPdfHeader(first);
        first.Add(new PdfText(40, 760, "01 Feb 25"));
        first.Add(new PdfText(100, 760, "First row"));
        first.Add(new PdfText(300, 760, "10.00"));

        List<PdfText> second = new List<PdfText>
        {
            new(70, 800, "Date"),
            new(130, 800, "Description"),
            new(330, 800, "Payments"),
            new(430, 800, "Deposits"),
            new(500, 800, "Bank"),
            new(530, 800, "fees"),
            new(560, 800, "Balance"),
            new(130, 770, "continued with incompatible bands"),
            new(70, 752, "02 Feb 25"),
            new(130, 752, "Second row"),
            new(330, 752, "20.00"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnknownTextOnFirstMiddleOrLastTablePageRejects(int residualPage)
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        for (var index = 0; index < 3; index++)
        {
            List<PdfText> texts = new List<PdfText>();
            AddPdfHeader(texts);
            texts.Add(new PdfText(40, 760, "01 Feb 2025"));
            texts.Add(new PdfText(100, 760, $"Row {index}"));
            texts.Add(new PdfText(300, 760, "10.00"));
            if (index == residualPage)
            {
                texts.Add(new PdfText(100, residualPage == 0 ? 820 : 600, "Residual"));
            }
            pdf.AddPage(texts);
        }

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void TextBesideOrBelowAFooterRejects()
    {
        List<PdfText> beside = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 30, "Page 1"),
            new(530, 30, "X"),
        };
        MinimalPdfBuilder besidePdf = new MinimalPdfBuilder();
        besidePdf.AddPage(beside);
        AssertRejected(besidePdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> below = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 30, "Page 1"),
            new(100, 20, "Residual below the footer"),
        };
        MinimalPdfBuilder belowPdf = new MinimalPdfBuilder();
        belowPdf.AddPage(below);
        AssertRejected(belowPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> inconsistent = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 30, "Page 2"),
        };
        MinimalPdfBuilder inconsistentPdf = new MinimalPdfBuilder();
        inconsistentPdf.AddPage(inconsistent);
        AssertRejected(inconsistentPdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void CompleteFooterControlsPass()
    {
        List<PdfText> single = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 30, "Page 1 of 1"),
        };
        MinimalPdfBuilder singlePdf = new MinimalPdfBuilder();
        singlePdf.AddPage(single);

        List<PdfText> first = new List<PdfText>();
        AddPdfHeader(first);
        first.Add(new PdfText(40, 760, "01 Feb 2025"));
        first.Add(new PdfText(100, 760, "First row"));
        first.Add(new PdfText(300, 760, "10.00"));
        first.Add(new PdfText(100, 30, "Page 1 of 2"));
        List<PdfText> second = new List<PdfText>();
        AddPdfHeader(second);
        second.Add(new PdfText(40, 760, "02 Feb 2025"));
        second.Add(new PdfText(100, 760, "Second row"));
        second.Add(new PdfText(300, 760, "20.00"));
        second.Add(new PdfText(100, 30, "Page 2 of 2"));
        MinimalPdfBuilder multiPdf = new MinimalPdfBuilder();
        multiPdf.AddPage(first);
        multiPdf.AddPage(second);

        var singleResult = StandardBankPdfParser.Parse(singlePdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);
        var multiResult = StandardBankPdfParser.Parse(multiPdf.Build(), null, null, null);

        Assert.Equal(3, singleResult.Rows.Count);
        Assert.Equal(2, multiResult.Rows.Count);
        Assert.All(multiResult.Rows, row => Assert.True(row.IsValid));
    }

    [Fact]
    public void HeaderAndPeriodSameLineResidueRejects()
    {
        List<PdfText> header = new List<PdfText>();
        AddPdfHeader(header);
        header.Add(new PdfText(560, 800, "X"));
        MinimalPdfBuilder headerPdf = new MinimalPdfBuilder();
        headerPdf.AddPage(header);
        AssertRejected(headerPdf.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder period = new MinimalPdfBuilder();
        period.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(40, 780, "Statement period: 01 Feb 2025 to 28 Feb 2025"), new PdfText(400, 780, "X"),]);
        AssertRejected(period.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void TerminalTotalSameLineResidueBecomesAnInvalidCandidate()
    {
        List<PdfText> texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
            new(530, 706, "X"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(4, result.Rows.Count);
        var total = Assert.Single(result.Rows, row => row.RawValues["description"] == "Total payments");
        Assert.Contains("invalid_date", total.ValidationCodes);
    }

    [Fact]
    public void BalancePositionAndShapeViolationsReject()
    {
        List<PdfText> openingAfter = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Opening balance"),
            new(530, 706, "1,000.00"),
        };
        MinimalPdfBuilder openingAfterPdf = new MinimalPdfBuilder();
        openingAfterPdf.AddPage(openingAfter);
        AssertRejected(openingAfterPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> closingBefore = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Closing balance"),
            new(530, 706, "28.50"),
            new(40, 688, "04 Feb 25"),
            new(100, 688, "Later row"),
            new(300, 688, "5.00"),
        };
        MinimalPdfBuilder closingBeforePdf = new MinimalPdfBuilder();
        closingBeforePdf.AddPage(closingBefore);
        AssertRejected(closingBeforePdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> arbitrary = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Merchant note"),
            new(530, 706, "1.00"),
        };
        MinimalPdfBuilder arbitraryPdf = new MinimalPdfBuilder();
        arbitraryPdf.AddPage(arbitrary);
        AssertRejected(arbitraryPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> balanceOnly = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(530, 706, "1.00"),
        };
        MinimalPdfBuilder balanceOnlyPdf = new MinimalPdfBuilder();
        balanceOnlyPdf.AddPage(balanceOnly);
        AssertRejected(balanceOnlyPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> garbageCell = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 770, "Opening balance"),
            new(530, 770, "garbage"),
        };
        MinimalPdfBuilder garbageCellPdf = new MinimalPdfBuilder();
        garbageCellPdf.AddPage(garbageCell);
        AssertRejected(garbageCellPdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void WrongOrMalformedTerminalTotalsBecomeInvalidCandidates()
    {
        List<PdfText> wrongBand = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(400, 706, "1.00"),
        };
        MinimalPdfBuilder wrongBandPdf = new MinimalPdfBuilder();
        wrongBandPdf.AddPage(wrongBand);
        var wrongBandResult = StandardBankPdfParser.Parse(wrongBandPdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);
        Assert.Equal(4, wrongBandResult.Rows.Count);
        Assert.Contains("invalid_date", Assert.Single(wrongBandResult.Rows, row => row.RawValues["description"] == "Total payments").ValidationCodes);

        List<PdfText> malformed = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "abc"),
        };
        MinimalPdfBuilder malformedPdf = new MinimalPdfBuilder();
        malformedPdf.AddPage(malformed);
        var malformedResult = StandardBankPdfParser.Parse(malformedPdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);
        Assert.Equal(4, malformedResult.Rows.Count);
        var malformedRow = Assert.Single(malformedResult.Rows, row => row.RawValues["description"] == "Total payments");
        Assert.Contains("invalid_date", malformedRow.ValidationCodes);
        Assert.Contains("invalid_amount", malformedRow.ValidationCodes);

        List<PdfText> multiple = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1.00"),
            new(400, 706, "2.00"),
        };
        MinimalPdfBuilder multiplePdf = new MinimalPdfBuilder();
        multiplePdf.AddPage(multiple);
        var multipleResult = StandardBankPdfParser.Parse(multiplePdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);
        Assert.Equal(4, multipleResult.Rows.Count);
        Assert.Contains("multiple_amounts", Assert.Single(multipleResult.Rows, row => row.RawValues["description"] == "Total payments").ValidationCodes);
    }

    [Fact]
    public void AnyRasterImageRejectsAsTextPdfRequired()
    {
        MinimalPdfBuilder cover = new MinimalPdfBuilder();
        cover.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(40, 780, "Synthetic statement")], 1, 50, 50);
        AssertRejected(cover.Build(), ParserReasons.TextPdfRequired);

        List<PdfText> header = new List<PdfText>();
        AddPdfHeader(header);
        header.Add(new PdfText(40, 760, "01 Feb 2025"));
        header.Add(new PdfText(100, 760, "Row"));
        header.Add(new PdfText(300, 760, "10.00"));
        MinimalPdfBuilder withHeader = new MinimalPdfBuilder();
        withHeader.AddPage(header, 1, 50, 50);
        AssertRejected(withHeader.Build(), ParserReasons.TextPdfRequired);

        MinimalPdfBuilder multiple = new MinimalPdfBuilder();
        multiple.AddPage(header, 3, 30, 30);
        AssertRejected(multiple.Build(), ParserReasons.TextPdfRequired);

        AssertRejected(new MinimalPdfBuilder().BuildImageOnly(3, 20, 20), ParserReasons.TextPdfRequired);
    }

    [Fact]
    public void FlowOnlyTextBelowTheOldCutoffBecomesACandidate()
    {
        List<PdfText> texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(300, 20, "12.34"),
        };
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(4, result.Rows.Count);
        var residual = result.Rows[^1];
        Assert.Contains("invalid_date", residual.ValidationCodes);
        Assert.Contains("empty_description", residual.Warnings);
        Assert.Equal(-1234, residual.AmountCents);
    }

    [Fact]
    public void ConflictingSecondHeaderRejectsBeforeStaleInterpretation()
    {
        List<PdfText> deposit = new List<PdfText>();
        AddPdfHeader(deposit);
        deposit.AddRange([new PdfText(40, 760, "01 Feb 2025"), new PdfText(100, 760, "First row"), new PdfText(300, 760, "10.00"),]);
        deposit.AddRange(HeaderAt(700, 40, 100, 220, 300, 400, 430, 500));
        deposit.AddRange([new PdfText(40, 680, "02 Feb 2025"), new PdfText(100, 680, "Intended deposit"), new PdfText(300, 680, "20.00"),]);
        MinimalPdfBuilder depositPdf = new MinimalPdfBuilder();
        depositPdf.AddPage(deposit);
        AssertRejected(depositPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> balance = new List<PdfText>();
        AddPdfHeader(balance);
        balance.AddRange([new PdfText(40, 760, "01 Feb 2025"), new PdfText(100, 760, "First row"), new PdfText(300, 760, "10.00"),]);
        balance.AddRange(HeaderAt(700, 40, 100, 220, 300, 400, 430, 500));
        balance.AddRange([new PdfText(40, 680, "02 Feb 2025"), new PdfText(100, 680, "Balance only"), new PdfText(500, 680, "9,999.99"),]);
        MinimalPdfBuilder balancePdf = new MinimalPdfBuilder();
        balancePdf.AddPage(balance);
        AssertRejected(balancePdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Theory]
    [InlineData("Account: PRIVATE_ACCOUNT_7731")]
    [InlineData("Customer: PRIVATE_ACCOUNT_7731")]
    [InlineData("Branch: PRIVATE_ACCOUNT_7731")]
    [InlineData("Account: PRIVATE_ACCOUNT_7731_EXTENDING_VALUE")]
    public void UnknownPreambleMetadataRejects(string metadata)
    {
        List<PdfText> texts = new List<PdfText>
        {
            new(40, 840, "Synthetic Bank"),
            new(40, 820, metadata),
        };
        AddPdfHeader(texts);
        texts.AddRange([new PdfText(40, 760, "01 Feb 2025"), new PdfText(100, 760, "Valid row"), new PdfText(300, 760, "10.00"),]);
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void NegativeXWordsNeverDisappear()
    {
        List<PdfText> rowTexts = new List<PdfText>();
        AddPdfHeader(rowTexts);
        rowTexts.AddRange([new PdfText(40, 760, "01 Feb 2025"), new PdfText(100, 760, "Valid row"), new PdfText(300, 760, "10.00"), new PdfText(-95, 760, "UNMAPPED_CANARY_4421"),]);
        MinimalPdfBuilder rowPdf = new MinimalPdfBuilder();
        rowPdf.AddPage(rowTexts);
        AssertRejected(rowPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> totalTexts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
            new(-95, 706, "UNMAPPED_CANARY_4421"),
        };
        MinimalPdfBuilder totalPdf = new MinimalPdfBuilder();
        totalPdf.AddPage(totalTexts);
        AssertRejected(totalPdf.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> headerTexts = new List<PdfText>();
        AddPdfHeader(headerTexts);
        headerTexts.Add(new PdfText(-95, 800, "UNMAPPED_CANARY_4421"));
        MinimalPdfBuilder headerPdf = new MinimalPdfBuilder();
        headerPdf.AddPage(headerTexts);
        AssertRejected(headerPdf.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder headerless = new MinimalPdfBuilder();
        headerless.AddPage([new PdfText(40, 800, "Synthetic Bank"), new PdfText(-95, 760, "UNMAPPED_CANARY_4421"),]);
        AssertRejected(headerless.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder period = new MinimalPdfBuilder();
        period.AddPage([new PdfText(40, 780, "Statement period: 01 Feb 2025 to 28 Feb 2025"), new PdfText(-95, 780, "UNMAPPED_CANARY_4421"),]);
        AssertRejected(period.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> footerTexts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 30, "Page 1 of 1"),
            new(-95, 30, "UNMAPPED_CANARY_4421"),
        };
        MinimalPdfBuilder footerPdf = new MinimalPdfBuilder();
        footerPdf.AddPage(footerTexts);
        AssertRejected(footerPdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void CrossPageContinuationBridgeIsBoundedAndConsumed()
    {
        static List<PdfText> FirstPage()
        {
            List<PdfText> texts = new List<PdfText>();
            AddPdfHeader(texts);
            texts.AddRange([new PdfText(40, 760, "01 Feb 2025"), new PdfText(100, 760, "First row"), new PdfText(300, 760, "10.00"),]);

            return texts;
        }

        static List<PdfText> SecondPage(params PdfText[] body)
        {
            List<PdfText> texts = new List<PdfText>();
            AddPdfHeader(texts);
            texts.AddRange(body);

            return texts;
        }

        MinimalPdfBuilder far = new MinimalPdfBuilder();
        far.AddPage(FirstPage());
        far.AddPage(SecondPage(new PdfText(100, 770, "first fragment"), new PdfText(100, 300, "Unrelated far text"), new PdfText(40, 270, "02 Feb 2025"), new PdfText(100, 270, "Second row"), new PdfText(300, 270, "20.00")));
        AssertRejected(far.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder distantFirst = new MinimalPdfBuilder();
        distantFirst.AddPage(FirstPage());
        distantFirst.AddPage(SecondPage(new PdfText(100, 300, "distant fragment"), new PdfText(40, 270, "02 Feb 2025"), new PdfText(100, 270, "Second row"), new PdfText(300, 270, "20.00")));
        AssertRejected(distantFirst.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder belowFooter = new MinimalPdfBuilder();
        belowFooter.AddPage(FirstPage());
        belowFooter.AddPage(SecondPage(new PdfText(100, 770, "continued"), new PdfText(100, 30, "Page 2 of 2"), new PdfText(100, 20, "Residual below footer")));
        AssertRejected(belowFooter.Build(), ParserReasons.UnsupportedLayout);

        List<PdfText> summaryPage = new List<PdfText> { new(100, 820, "Summary") };
        AddPdfHeader(summaryPage);
        summaryPage.AddRange([new PdfText(100, 770, "continued after summary"), new PdfText(40, 752, "02 Feb 2025"), new PdfText(100, 752, "Second row"), new PdfText(300, 752, "20.00"),]);
        MinimalPdfBuilder summary = new MinimalPdfBuilder();
        summary.AddPage(FirstPage());
        summary.AddPage(summaryPage);
        AssertRejected(summary.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void ToleratedHeaderShiftCannotChangeASemanticColumn()
    {
        List<PdfText> texts = new List<PdfText>();
        AddPdfHeader(texts);
        texts.AddRange([new PdfText(40, 760, "01 Feb 2025"), new PdfText(100, 760, "First row"), new PdfText(300, 760, "10.00"),]);
        texts.AddRange(HeaderAt(700, 40, 100, 300, 404, 470, 500, 530));
        texts.AddRange([new PdfText(40, 680, "02 Feb 2025"), new PdfText(100, 680, "Shifted boundary"), new PdfText(359, 680, "10.00"),]);
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void HeaderlessHeadingsRequireTheTopOfPagePosition()
    {
        var first = StatementFixtureBuilder.HandAuthoredSmallPdfTexts();

        foreach (var text in new[] { "Summary", "Synthetic Bank", "Statement period: 01 Jan 2025 to 31 Jan 2025" })
        {
            MinimalPdfBuilder pdf = new MinimalPdfBuilder();
            pdf.AddPage(first);
            pdf.AddPage([new PdfText(100, 400, text)]);
            AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
        }

        MinimalPdfBuilder justOutside = new MinimalPdfBuilder();
        justOutside.AddPage(first);
        justOutside.AddPage([new PdfText(100, 710, "Summary")]);
        AssertRejected(justOutside.Build(), ParserReasons.UnsupportedLayout);

        MinimalPdfBuilder residue = new MinimalPdfBuilder();
        residue.AddPage(first);
        residue.AddPage([new PdfText(100, 780, "Summary"), new PdfText(500, 780, "RESIDUE"),]);
        AssertRejected(residue.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void GenericMappingCannotStartInsideAnFnbPreamble()
    {
        const string csv = "Name,Description,Amount\n" + "private-two\nprivate-three\nprivate-four\nprivate-five\nprivate-six\n" + "Date,Amount,Balance,Description\n" + "2025/01/02,-10.00,0.00,Synthetic merchant\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void GenericMappingCannotStartInsideFnbPreambleWithMalformedHeader()
    {
        const string csv = "Name,Description,Amount\n" + "PRIVATE_TWO\nPRIVATE_THREE\nPRIVATE_FOUR\nPRIVATE_FIVE\nPRIVATE_SIX\n" + "Date,Amount,Balance,Details\n" + "2025/01/02,-10.00,0.00,Synthetic merchant\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 3,
            Delimiter = ",",
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void GenericMappingAtFnbHeaderSuppressesThePreamble()
    {
        const string csv = "Name,Description,Amount\n" + "PRIVATE_TWO\nPRIVATE_THREE\nPRIVATE_FOUR\nPRIVATE_FIVE\nPRIVATE_SIX\n" + "Date,Amount,Balance,Description\n" + "2025/01/02,-10.00,0.00,Synthetic merchant\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 7,
            Delimiter = ",",
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 3,
            AmountColumn = 1,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null);
        Assert.Single(result.Rows);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Equal("Synthetic merchant", result.Rows[0].Description);
    }

    [Theory]
    [InlineData("|")]
    [InlineData(";")]
    public void GenericAlternateDelimiterCannotSelectAHeaderInsideAnFnbPreamble(string delimiter)
    {
        var csv =
            "Synthetic FNB export\n" +
            "Statement period: 01 Jan 2025 to 31 Jan 2025\n" +
            $"Name{delimiter}PRIVATE_NAME\n" +
            $"Account{delimiter}PRIVATE_ACCOUNT\n" +
            $"Balance{delimiter}PRIVATE_BALANCE\n" +
            "Synthetic only\n" +
            "Date,Amount,Balance,Description\n" +
            "2025/01/02,-10.00,0.00,Good row\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 3,
            Delimiter = delimiter,
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            AmountColumn = 0,
            ConfirmEmptyDescription = true,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void QuotedFnbPreambleLabelsCannotBypassGenericSuppression()
    {
        const string csv = "FNB export\n" + "\n" + "\"Name\",PRIVATE_NAME\n" + "\"Account\",PRIVATE_ACCOUNT\n" + "\"Balance\",PRIVATE_BALANCE\n" + "\n" + "Date,Amount,Balance,Description\n" + "2025/01/02,-10.00,0.00,Shop\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = "|",
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            AmountColumn = 0,
            ConfirmEmptyDescription = true,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void DescriptionFirstGenericStatementIsNotMisclassifiedAsAnFnbPreamble()
    {
        const string csv = "Description|Date|Amount\n" + "Account fee|2025-01-02|-1.00\n" + "Name change|2025-01-03|-2.00\n" + "Balance adjustment|2025-01-04|-3.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = "|",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 1,
            DescriptionColumn = 0,
            AmountColumn = 2,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Equal(3, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.True(row.IsValid));
    }

    [Fact]
    public void GenericAlternateDelimiterStillSuppressesTheFnbPreambleAtTheSeventhLine()
    {
        const string csv =
            "Synthetic FNB export\n" +
            "Statement period: 01 Jan 2025 to 31 Jan 2025\n" +
            "Name|PRIVATE_NAME\n" +
            "Account|PRIVATE_ACCOUNT\n" +
            "Balance|PRIVATE_BALANCE\n" +
            "Synthetic only\n" +
            "Date,Amount,Balance,Description\n" +
            "2025/01/02,-10.00,0.00,Good row\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 7,
            Delimiter = "|",
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            AmountColumn = 0,
            ConfirmEmptyDescription = true,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void GenericMalformedFnbHeaderDoesNotBypassPreambleSuppression()
    {
        const string csv =
            "Synthetic FNB export\n" +
            "Statement period: 01 Jan 2025 to 31 Jan 2025\n" +
            "Name|PRIVATE_NAME\n" +
            "Account|PRIVATE_ACCOUNT\n" +
            "Balance|PRIVATE_BALANCE\n" +
            "Synthetic only\n" +
            "Date,Amount,Balance,Desc\n" +
            "2025/01/02,-10.00,0.00,Good row\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 3,
            Delimiter = "|",
            DateFormat = "yyyy/MM/dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            AmountColumn = 0,
            ConfirmEmptyDescription = true,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void ConfiguredGenericParseReturnsTheConfirmedNegativeStyle()
    {
        const string csv = "Date,Description,Amount\n2025-01-02,Shop,(10.00)\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
            NegativeStyle = "parentheses",
        };

        var result = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Equal("parentheses", result.MappingSuggestions["negative_style"]);
        Assert.Equal(-1000, result.Rows[0].AmountCents);
    }

    [Fact]
    public void GenericRowsMustMatchTheSelectedHeaderWidth()
    {
        const string csv = "Date,Description,Amount\n2025-01-02,Shop,10,20.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Contains("invalid_field_count", result.Rows[0].ValidationCodes);
    }

    [Fact]
    public void GenericMalformedHeaderIsRejected()
    {
        const string csv = "\"Da\"te,Description,Amount\n2025-01-02,Shop,-1.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void GenericMalformedQuotingIsInvalid()
    {
        const string csv = "Date,Description,Amount\n2025-01-02,\"Shop\"garbage,-10.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Contains("invalid_field_count", result.Rows[0].ValidationCodes);
    }

    [Fact]
    public void GenericNegativeStyleIsEnforced()
    {
        const string csv = "Date,Description,Amount\n" + "2025-01-02,Leading,-10.00\n" + "2025-01-02,Parentheses,(10.00)\n" + "2025-01-02,CrDr,10.00 DR\n" + "2025-01-02,Trailing,10.00-\n";
        ParserConfig baseConfig = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var parentheses = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), baseConfig with { NegativeStyle = "parentheses" }, null, null);
        Assert.Contains("invalid_sign", parentheses.Rows[0].ValidationCodes);
        Assert.Equal(-1000, parentheses.Rows[1].AmountCents);
        Assert.Equal(-1000, parentheses.Rows[2].AmountCents);

        var crDr = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), baseConfig with { NegativeStyle = "cr_dr" }, null, null);
        Assert.Contains("invalid_sign", crDr.Rows[0].ValidationCodes);
        Assert.Equal(-1000, crDr.Rows[2].AmountCents);

        var trailing = CsvStatementParsers.ParseGeneric(System.Text.Encoding.UTF8.GetBytes(csv), baseConfig with { NegativeStyle = "trailing_minus" }, null, null);
        Assert.Equal(-1000, trailing.Rows[3].AmountCents);
    }

    private static List<PdfText> HeaderAt(double y, params double[] xs)
    {
        var labels = new[]
        {
            "Date", "Description", "Payments", "Deposits", "Bank", "fees", "Balance",
        };
        Assert.Equal(labels.Length, xs.Length);

        return labels.Select((label, index) => new PdfText(xs[index], y, label)).ToList();
    }

    private static void AddPdfHeader(List<PdfText> texts, bool includeFees = true)
    {
        texts.Add(new PdfText(40, 800, "Date"));
        texts.Add(new PdfText(100, 800, "Description"));
        texts.Add(new PdfText(300, 800, "Payments"));
        texts.Add(new PdfText(400, 800, "Deposits"));
        texts.Add(new PdfText(470, 800, "Bank"));
        if (includeFees)
        {
            texts.Add(new PdfText(500, 800, "fees"));
        }
        texts.Add(new PdfText(530, 800, "Balance"));
    }

    [Fact]
    public void PdfTransactionPageWithoutHeaderIsRejected()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> first = new List<PdfText>();
        AddPdfHeader(first);
        first.Add(new PdfText(40, 760, "01 Jan 25"));
        first.Add(new PdfText(100, 760, "Header page"));
        first.Add(new PdfText(300, 760, "10.00"));
        pdf.AddPage(first);

        pdf.AddPage([new PdfText(40, 760, "02 Jan 25"), new PdfText(100, 760, "Headerless transaction page"), new PdfText(300, 760, "-20.00"), new PdfText(530, 760, "-10.00"),]);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void PdfHeaderMissingFeesIsRejected()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> texts = new List<PdfText>();
        AddPdfHeader(texts, includeFees: false);
        texts.Add(new PdfText(40, 760, "01 Jan 25"));
        texts.Add(new PdfText(100, 760, "Missing fees label"));
        texts.Add(new PdfText(300, 760, "10.00"));
        pdf.AddPage(texts);

        AssertRejected(pdf.Build(), ParserReasons.UnsupportedLayout);
    }

    [Fact]
    public void PdfMalformedDateRowIsRetainedAsInvalid()
    {
        MinimalPdfBuilder pdf = new MinimalPdfBuilder();
        List<PdfText> texts = new List<PdfText>();
        AddPdfHeader(texts);
        texts.Add(new PdfText(40, 760, "01 Jan 25"));
        texts.Add(new PdfText(100, 760, "Valid row"));
        texts.Add(new PdfText(300, 760, "10.00"));
        texts.Add(new PdfText(40, 740, "2025/01/02"));
        texts.Add(new PdfText(100, 740, "Malformed date row"));
        texts.Add(new PdfText(300, 740, "11.00"));
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains("invalid_date", result.Rows[1].ValidationCodes);
        Assert.Equal("2025/01/02", result.Rows[1].RawValues["date"]);
    }

    [Fact]
    public void GenericMissingDescriptionColumnNeedsExplicitEmptyConfirmation()
    {
        const string csv = "Date,Amount\n2025-01-02,-1.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = null,
            AmountColumn = 1,
            ConfirmEmptyDescription = false,
        };

        var exception = Assert.Throws<StatementRejectedException>(() => CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);

        var confirmed = config with { ConfirmEmptyDescription = true };
        var result = CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), confirmed, null, null);
        Assert.Equal("No description", result.Rows[0].Description);
        Assert.Equal(string.Empty, result.Rows[0].NormalizedDescription);
        Assert.Contains("empty_description", result.Rows[0].Warnings);
    }

    [Fact]
    public void GenericTwoDigitDateWithoutPeriodCarriesCenturyWarning()
    {
        const string csv = "Date,Description,Amount\n01/02/25,Coffee,-1.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "dd/MM/yy",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Null(result.Rows[0].TransactionDate);
        Assert.Contains("century_required", result.Rows[0].Warnings);
    }

    [Fact]
    public void GenericOutOfPeriodRowIsInvalid()
    {
        const string csv = "Date,Description,Amount\n2025-02-01,Outside,-1.00\n";
        ParserConfig config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = 0,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));

        Assert.Contains("date_outside_period", result.Rows[0].ValidationCodes);
    }

    private static void AssertRejected(byte[] bytes, string reason)
    {
        var exception = Assert.Throws<StatementRejectedException>(() => reason == ParserReasons.UnsafeArchive ? CsvStatementParsers.ParseFnb(bytes, null, null) : StandardBankPdfParser.Parse(bytes, null, null, null));
        Assert.Equal(reason, exception.Reason);
    }
}
