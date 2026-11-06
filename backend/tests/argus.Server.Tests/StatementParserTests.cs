using System.Globalization;
using System.Text;
using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class StatementParserTests
{
    [Fact]
    public void FnbBareAndZipProduceIdenticalRows()
    {
        var bare = StatementFixtureBuilder.BuildFnbCsv(150, chequeAccount: false);
        var zip = StatementFixtureBuilder.BuildFnbZip(150, chequeAccount: false);

        var bareResult = CsvStatementParsers.ParseFnb(bare.Bytes, null, null);
        var zipResult = CsvStatementParsers.ParseFnb(zip.Bytes, null, null);

        Assert.Equal(StatementFormat.FnbCsv, bareResult.Format);
        Assert.Equal(StatementFormat.FnbZip, zipResult.Format);
        Assert.Equal(150, bareResult.Rows.Count);
        Assert.Equal(150, zipResult.Rows.Count);
        for (var index = 0; index < 150; index++)
        {
            Assert.Equal(bare.Rows[index].Date, bareResult.Rows[index].TransactionDate?.ToString("yyyy-MM-dd"));
            Assert.Equal(bare.Rows[index].Description, bareResult.Rows[index].Description);
            Assert.Equal(bare.Rows[index].Cents, bareResult.Rows[index].AmountCents);
            Assert.Equal(bareResult.Rows[index].TransactionDate, zipResult.Rows[index].TransactionDate);
            Assert.Equal(bareResult.Rows[index].Description, zipResult.Rows[index].Description);
            Assert.Equal(bareResult.Rows[index].AmountCents, zipResult.Rows[index].AmountCents);
        }
    }

    [Fact]
    public void FnbChequeAndCardStructuresParse()
    {
        var card = StatementFixtureBuilder.BuildFnbCsv(199, chequeAccount: false);
        var cheque = StatementFixtureBuilder.BuildFnbCsv(199, chequeAccount: true);

        var cardResult = CsvStatementParsers.ParseFnb(card.Bytes, null, null);
        var chequeResult = CsvStatementParsers.ParseFnb(cheque.Bytes, null, null);

        Assert.Equal(199, cardResult.Rows.Count);
        Assert.Equal(199, chequeResult.Rows.Count);
        Assert.All(cardResult.Rows, row => Assert.True(row.IsValid));
        Assert.All(chequeResult.Rows, row => Assert.True(row.IsValid));
    }

    [Fact]
    public void StandardBankFourPageFixtureParsesEveryRow()
    {
        var fixture = StatementFixtureBuilder.BuildStandardBankPdf(4, 49, 66);
        var result = StandardBankPdfParser.Parse(
            fixture.Bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            null);

        Assert.Equal(49, result.Rows.Count);
        Assert.Equal(new DateOnly(2025, 1, 1), result.ProposedStartDate);
        Assert.Equal(new DateOnly(2025, 1, 31), result.ProposedEndDate);
        for (var index = 0; index < fixture.Rows.Count; index++)
        {
            var expected = fixture.Rows[index];
            var actual = result.Rows[index];
            Assert.Equal(expected.Date, actual.TransactionDate?.ToString("yyyy-MM-dd"));
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Cents, actual.AmountCents);
            Assert.Equal(expected.Page, actual.SourcePage);
            Assert.Equal(expected.ValidationCodes, actual.ValidationCodes.ToArray());
        }
    }

    [Fact]
    public void StandardBankEighteenPageFixtureParsesEveryRow()
    {
        var fixture = StatementFixtureBuilder.BuildStandardBankPdf(18, 297, 356);
        var result = StandardBankPdfParser.Parse(
            fixture.Bytes,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31),
            null);

        Assert.Equal(297, result.Rows.Count);
        for (var index = 0; index < fixture.Rows.Count; index++)
        {
            var expected = fixture.Rows[index];
            var actual = result.Rows[index];
            Assert.Equal(expected.Date, actual.TransactionDate?.ToString("yyyy-MM-dd"));
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Cents, actual.AmountCents);
            Assert.Equal(expected.ValidationCodes, actual.ValidationCodes.ToArray());
        }
    }

    [Fact]
    public void HandAuthoredSmallPdfParses()
    {
        var result = StandardBankPdfParser.Parse(
            StatementFixtureBuilder.BuildHandAuthoredSmallPdf(),
            new DateOnly(2025, 2, 1),
            new DateOnly(2025, 2, 28),
            null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(-1000, result.Rows[0].AmountCents);
        Assert.Equal(2000, result.Rows[1].AmountCents);
        Assert.Equal(-150, result.Rows[2].AmountCents);
    }

    [Fact]
    public void CrossYearPeriodResolvesTwoDigitYears()
    {
        var fixture = StatementFixtureBuilder.BuildStandardBankPdf(
            4, 49, 0, includePeriod: true, crossYear: true, reorderedStreams: false);
        var result = StandardBankPdfParser.Parse(
            fixture.Bytes,
            new DateOnly(2023, 12, 15),
            new DateOnly(2024, 1, 15),
            null);

        Assert.Equal(new DateOnly(2023, 12, 15), result.ProposedStartDate);
        Assert.Equal(new DateOnly(2024, 1, 15), result.ProposedEndDate);
        Assert.Contains(result.Rows, row => row.TransactionDate?.Year == 2023);
        Assert.Contains(result.Rows, row => row.TransactionDate?.Year == 2024);
    }

    [Fact]
    public void MissingPeriodRequiresConfirmation()
    {
        var fixture = StatementFixtureBuilder.BuildStandardBankPdf(4, 49, 0, includePeriod: false);
        var result = StandardBankPdfParser.Parse(fixture.Bytes, null, null, null);

        Assert.Null(result.ProposedStartDate);
        Assert.Null(result.ProposedEndDate);
        Assert.Contains("start_date", result.RequiredConfirmations);
        Assert.Contains("end_date", result.RequiredConfirmations);
    }

    [Fact]
    public void GenericCsvMapsSignedAmountsAndQuotedMultilineDescriptions()
    {
        const string csv = "Date,Description,Amount\n" +
                           "2025-01-02,\"Coffee, large\",-12.34\n" +
                           "2025-01-03,\"Multi\nline note\",100.00\n" +
                           "2025-01-04,\"  Cafe  Corner\tnote  \",-5.00\n";
        var config = new ParserConfig
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

        var result = CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(-1234, result.Rows[0].AmountCents);
        Assert.Equal("Coffee, large", result.Rows[0].Description);
        Assert.Equal(10000, result.Rows[1].AmountCents);
        Assert.Equal("Multi\nline note", result.Rows[1].Description);
        Assert.Equal("multi line note", result.Rows[1].NormalizedDescription);
        Assert.Equal(-500, result.Rows[2].AmountCents);
        Assert.Equal("Cafe  Corner\tnote", result.Rows[2].Description);
        Assert.Equal("cafe corner note", result.Rows[2].NormalizedDescription);
    }

    [Fact]
    public void FnbCsvPreservesInternalDescriptionWhitespaceAndTrimsPadding()
    {
        const string csv = "one\ntwo\nthree\nfour\nfive\nsix\n" +
                           "Date,Amount,Balance,Description\n" +
                           "2024/01/02,-10.00,0.00,Coffee   Shop\tNote   \n";
        var result = CsvStatementParsers.ParseFnb(
            Encoding.UTF8.GetBytes(csv), null, null);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Coffee   Shop\tNote", row.Description);
        Assert.Equal("coffee shop note", row.NormalizedDescription);
        Assert.Equal("Coffee   Shop\tNote   ", row.RawValues["description"]);
    }

    [Fact]
    public void FnbMalformedHeaderIsRejected()
    {
        const string csv = "one\ntwo\nthree\nfour\nfive\nsix\n" +
                           "\"Date\" ,Amount,Balance,Description\n" +
                           "2024/01/02,-10.00,0.00,Coffee\n";

        var exception = Assert.Throws<StatementRejectedException>(() =>
            CsvStatementParsers.ParseFnb(Encoding.UTF8.GetBytes(csv), null, null));
        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void GenericCsvRejectsNegativeColumnIndexes()
    {
        const string csv = "Date,Description,Amount\n2025-01-02,Shop,-1.00\n";
        var config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "signed",
            DateColumn = -1,
            DescriptionColumn = 1,
            AmountColumn = 2,
            Direction = "normal",
        };

        var exception = Assert.Throws<StatementRejectedException>(() =>
            CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, null, null));
        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void GenericDebitAndCreditColumnsAreExclusive()
    {
        const string csv = "Date,Description,Debit,Credit\n" +
                           "2025-01-02,Outgoing,10.00,\n" +
                           "2025-01-03,Incoming,,20.00\n" +
                           "2025-01-04,Both,1.00,2.00\n" +
                           "2025-01-05,Neither,,\n";
        var config = new ParserConfig
        {
            Format = "generic",
            HeaderLine = 1,
            Delimiter = ",",
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            ThousandsSeparator = "none",
            AmountMode = "debit_credit",
            DateColumn = 0,
            DescriptionColumn = 1,
            DebitColumn = 2,
            CreditColumn = 3,
            Direction = "normal",
        };

        var result = CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Equal(-1000, result.Rows[0].AmountCents);
        Assert.Equal(2000, result.Rows[1].AmountCents);
        Assert.Contains("multiple_amounts", result.Rows[2].ValidationCodes);
        Assert.Contains("missing_amount", result.Rows[3].ValidationCodes);
    }

    [Fact]
    public void GenericTwoDigitDatesResolveAgainstConfirmedPeriod()
    {
        const string csv = "Date,Description,Amount\n01/12/23,December,-1.00\n05/01/24,January,2.00\n";
        var config = new ParserConfig
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

        var result = CsvStatementParsers.ParseGeneric(
            Encoding.UTF8.GetBytes(csv),
            config,
            new DateOnly(2023, 12, 1),
            new DateOnly(2024, 1, 31));

        Assert.Equal(new DateOnly(2023, 12, 1), result.Rows[0].TransactionDate);
        Assert.Equal(new DateOnly(2024, 1, 5), result.Rows[1].TransactionDate);
    }

    [Fact]
    public void GenericInvalidAmountsNeverRoundOrTruncate()
    {
        const string csv = "Date,Description,Amount\n2025-01-02,Too precise,1.234\n2025-01-03,Overflow,10000000000000000.00\n";
        var config = new ParserConfig
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

        var result = CsvStatementParsers.ParseGeneric(Encoding.UTF8.GetBytes(csv), config, null, null);

        Assert.Contains("invalid_amount", result.Rows[0].ValidationCodes);
        Assert.Null(result.Rows[0].AmountCents);
        Assert.Contains("invalid_amount", result.Rows[1].ValidationCodes);
        Assert.Null(result.Rows[1].AmountCents);
    }

    [Fact]
    public void GenericWithoutMappingIsRejectedForPreparation()
    {
        var bytes = Encoding.UTF8.GetBytes("Date,Description,Amount\n2025-01-02,Coffee,-1.00\n");

        var exception = Assert.Throws<StatementRejectedException>(() =>
            CsvStatementParsers.ParseGeneric(bytes, new ParserConfig(), null, null));

        Assert.Equal(ParserReasons.FormatConfirmationRequired, exception.Reason);
    }

    [Fact]
    public void ZipWithMultipleEntriesIsRejected()
    {
        using var output = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
                   output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var first = archive.CreateEntry("one.csv").Open())
            {
                first.Write(Encoding.UTF8.GetBytes("Date,Amount,Balance,Description\n"));
            }
            using (var second = archive.CreateEntry("two.csv").Open())
            {
                second.Write(Encoding.UTF8.GetBytes("Date,Amount,Balance,Description\n"));
            }
        }

        var exception = Assert.Throws<StatementRejectedException>(() =>
            CsvStatementParsers.ParseFnb(output.ToArray(), null, null));

        Assert.Equal(ParserReasons.UnsafeArchive, exception.Reason);
    }

    [Fact]
    public void NonPdfBytesAreRejectedSafely()
    {
        var exception = Assert.Throws<StatementRejectedException>(() =>
            StandardBankPdfParser.Parse(Encoding.UTF8.GetBytes("not a pdf"), null, null, null));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public void PrecisionAndGroupingAreStrict()
    {
        Assert.True(StatementAmounts.TryParseMagnitude("1,234.56", ".", ",", out var cents, out _, out _));
        Assert.Equal(123456, cents);
        Assert.False(StatementAmounts.TryParseMagnitude("1,23.56", ".", ",", out _, out _, out _));
        Assert.False(StatementAmounts.TryParseMagnitude("1.234", ".", ",", out _, out _, out _));
        Assert.True(StatementAmounts.TryParseSignedDecimal("-12.30", out var signed));
        Assert.Equal(-1230, signed);
        Assert.False(StatementAmounts.TryParseSignedDecimal("1,000.00", out _));
    }

    [Fact]
    public void PeriodOnlyPagesBeforeAndAfterATableAreHarmless()
    {
        var period = new PdfText(40, 780, "Statement period: 01 Feb 2025 to 28 Feb 2025");
        var before = new MinimalPdfBuilder();
        before.AddPage([period]);
        before.AddPage(StatementFixtureBuilder.HandAuthoredSmallPdfTexts());
        var beforeResult = StandardBankPdfParser.Parse(
            before.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        var after = new MinimalPdfBuilder();
        after.AddPage(StatementFixtureBuilder.HandAuthoredSmallPdfTexts());
        after.AddPage([period]);
        var afterResult = StandardBankPdfParser.Parse(
            after.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, beforeResult.Rows.Count);
        Assert.Equal(3, afterResult.Rows.Count);
        Assert.Equal(new DateOnly(2025, 2, 1), beforeResult.ProposedStartDate);
        Assert.Equal(new DateOnly(2025, 2, 28), afterResult.ProposedEndDate);
    }

    [Fact]
    public void OpeningAndClosingBalanceRowsAreExcluded()
    {
        var texts = new List<PdfText>
        {
            new(40, 820, "Synthetic Bank"),
            new(40, 800, "Statement period: 01 Feb 2025 to 28 Feb 2025"),
            new(40, 780, "Date"),
            new(100, 780, "Description"),
            new(300, 780, "Payments"),
            new(400, 780, "Deposits"),
            new(470, 780, "Bank"),
            new(500, 780, "fees"),
            new(530, 780, "Balance"),
            new(100, 770, "Opening balance"),
            new(530, 770, "1,000.00"),
            new(40, 760, "01 Feb 25"),
            new(100, 760, "Hand written one"),
            new(300, 760, "10.00"),
            new(530, 760, "10.00"),
            new(40, 742, "02 Feb 25"),
            new(100, 742, "Hand written two"),
            new(400, 742, "20.00"),
            new(530, 742, "30.00"),
            new(40, 724, "03 Feb 25"),
            new(100, 724, "Hand written three"),
            new(470, 724, "1.50"),
            new(530, 724, "28.50"),
            new(100, 706, "Closing balance"),
            new(530, 706, "28.50"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(-1000, result.Rows[0].AmountCents);
        Assert.Equal(2000, result.Rows[1].AmountCents);
        Assert.Equal(-150, result.Rows[2].AmountCents);
    }

    [Fact]
    public void BroughtAndCarriedForwardLabelsAreExcludedPositionally()
    {
        var texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 770, "Balance brought forward"),
            new(530, 770, "1,000.00"),
            new(100, 706, "Balance carried forward"),
            new(530, 706, "28.50"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
    }

    [Theory]
    [InlineData("Total payments", 300)]
    [InlineData("Total deposits", 400)]
    [InlineData("Total bank fees", 470)]
    public void TerminalTotalsForEachFlowBandAreExcluded(string label, double flowX)
    {
        var texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, label),
            new(flowX, 706, "1,234.56"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void TerminalTotalBeforeALaterTransactionBecomesAnInvalidCandidate()
    {
        var texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
            new(40, 688, "04 Feb 25"),
            new(100, 688, "Later row"),
            new(300, 688, "5.00"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(5, result.Rows.Count);
        var total = Assert.Single(result.Rows, row => row.RawValues["description"] == "Total payments");
        Assert.Contains("invalid_date", total.ValidationCodes);
        Assert.Equal(-123456, total.AmountCents);
        Assert.Equal("04 Feb 25", result.Rows[4].RawValues["date"]);
    }

    [Fact]
    public void DatedSummaryAndTotalDescriptionsRemainTransactions()
    {
        var texts = new List<PdfText>
        {
            new(40, 800, "Date"),
            new(100, 800, "Description"),
            new(300, 800, "Payments"),
            new(400, 800, "Deposits"),
            new(470, 800, "Bank"),
            new(500, 800, "fees"),
            new(530, 800, "Balance"),
            new(40, 760, "01 Feb 25"),
            new(100, 760, "Total payments"),
            new(300, 760, "10.00"),
            new(40, 742, "01 Feb 25"),
            new(100, 742, "Summary"),
            new(400, 742, "20.00"),
            new(40, 724, "01 Feb 25"),
            new(100, 724, "Page 2 of 3"),
            new(300, 724, "30.00"),
            new(40, 706, "01 Feb 25"),
            new(100, 706, "Synthetic Bank"),
            new(400, 706, "40.00"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(4, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.True(row.IsValid));
        Assert.Equal("Total payments", result.Rows[0].Description);
        Assert.Equal("Summary", result.Rows[1].Description);
        Assert.Equal("Page 2 of 3", result.Rows[2].Description);
        Assert.Equal("Synthetic Bank", result.Rows[3].Description);
    }

    [Fact]
    public void EveryDateAndFlowCombinationRemainsACandidate()
    {
        var texts = new List<PdfText>
        {
            new(40, 800, "Date"),
            new(100, 800, "Description"),
            new(300, 800, "Payments"),
            new(400, 800, "Deposits"),
            new(470, 800, "Bank"),
            new(500, 800, "fees"),
            new(530, 800, "Balance"),
            new(40, 760, "01 Feb 25"),
            new(100, 760, "Grouped"),
            new(300, 760, "1,234.56"),
            new(40, 742, "02 Feb 25"),
            new(100, 742, "Ungrouped"),
            new(300, 742, "1234.56"),
            new(40, 724, "03 Feb 25"),
            new(100, 724, "Integer"),
            new(300, 724, "20"),
            new(40, 706, "04 Feb 25"),
            new(100, 706, "Garbage"),
            new(300, 706, "abc"),
            new(40, 688, "05 Feb 25"),
            new(100, 688, "Missing"),
            new(40, 670, "06 Feb 25"),
            new(100, 670, "Zero"),
            new(300, 670, "0.00"),
            new(40, 652, "2025/02/07"),
            new(100, 652, "Malformed date"),
            new(300, 652, "10.00"),
            new(300, 634, "10.00"),
            new(40, 616, "08 Feb 25"),
            new(100, 616, "Balance only"),
            new(530, 616, "99.99"),
            new(40, 598, "09 Feb 25"),
            new(100, 598, "Multiple"),
            new(300, 598, "1.00"),
            new(400, 598, "2.00"),
            new(40, 580, "10 Feb 25"),
            new(100, 580, "Multiple with zero"),
            new(300, 580, "0.00"),
            new(400, 580, "5.00"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(11, result.Rows.Count);
        Assert.Equal(-123456, result.Rows[0].AmountCents);
        Assert.Equal(-123456, result.Rows[1].AmountCents);
        Assert.Equal(-2000, result.Rows[2].AmountCents);
        Assert.Contains("invalid_amount", result.Rows[3].ValidationCodes);
        Assert.Null(result.Rows[3].AmountCents);
        Assert.Contains("missing_amount", result.Rows[4].ValidationCodes);
        Assert.Equal(0, result.Rows[5].AmountCents);
        Assert.Contains("invalid_date", result.Rows[6].ValidationCodes);
        Assert.Equal(-1000, result.Rows[6].AmountCents);
        var flowOnly = result.Rows[7];
        Assert.Contains("invalid_date", flowOnly.ValidationCodes);
        Assert.Contains("empty_description", flowOnly.Warnings);
        Assert.Contains("missing_amount", result.Rows[8].ValidationCodes);
        Assert.Null(result.Rows[8].AmountCents);
        Assert.Contains("multiple_amounts", result.Rows[9].ValidationCodes);
        Assert.Contains("multiple_amounts", result.Rows[10].ValidationCodes);
    }

    [Fact]
    public void SingleWordBankFeesHeaderParses()
    {
        var texts = new List<PdfText>
        {
            new(40, 800, "Date"),
            new(100, 800, "Description"),
            new(300, 800, "Payments"),
            new(400, 800, "Deposits"),
            new(470, 800, "Bankfees"),
            new(530, 800, "Balance"),
            new(40, 760, "01 Feb 25"),
            new(100, 760, "Single header row"),
            new(300, 760, "10.00"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Single(result.Rows);
        Assert.Equal(-1000, result.Rows[0].AmountCents);
    }

    [Fact]
    public void SummaryWordsInAContinuationRemainDescriptionText()
    {
        var texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 752, "Summary page 2"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("Hand written one Summary page 2", result.Rows[0].Description);
    }

    [Fact]
    public void CrossPageContinuationAttachesBeforeTheNextPagesFirstRow()
    {
        var first = new List<PdfText>
        {
            new(40, 800, "Date"),
            new(100, 800, "Description"),
            new(300, 800, "Payments"),
            new(400, 800, "Deposits"),
            new(470, 800, "Bank"),
            new(500, 800, "fees"),
            new(530, 800, "Balance"),
            new(40, 760, "01 Feb 25"),
            new(100, 760, "First row"),
            new(300, 760, "10.00"),
        };
        var second = new List<PdfText>
        {
            new(40, 800, "Date"),
            new(100, 800, "Description"),
            new(300, 800, "Payments"),
            new(400, 800, "Deposits"),
            new(470, 800, "Bank"),
            new(500, 800, "fees"),
            new(530, 800, "Balance"),
            new(100, 770, "continued across pages"),
            new(40, 752, "02 Feb 25"),
            new(100, 752, "Second row"),
            new(300, 752, "20.00"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("First row continued across pages", result.Rows[0].Description);
        Assert.Equal("Second row", result.Rows[1].Description);
    }

    [Fact]
    public void CompatibleRepeatedHeadersAreConsumedWithoutExtraRows()
    {
        var texts = Header(800, 40, 100, 300, 400, 470, 500, 530);
        texts.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "First row"),
            new PdfText(300, 760, "10.00"),
        ]);
        texts.AddRange(Header(700, 40, 100, 300, 400, 470, 500, 530));
        texts.AddRange(
        [
            new PdfText(40, 680, "02 Feb 2025"),
            new PdfText(100, 680, "Second row"),
            new PdfText(300, 680, "20.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.True(row.IsValid));
        Assert.Equal(-1000, result.Rows[0].AmountCents);
        Assert.Equal(-2000, result.Rows[1].AmountCents);
    }

    [Fact]
    public void ShiftedCompatibleRepeatedHeaderPreservesColumnAssignments()
    {
        var texts = Header(800, 40, 100, 300, 400, 470, 500, 530);
        texts.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "First row"),
            new PdfText(300, 760, "10.00"),
        ]);
        texts.AddRange(Header(700, 45, 105, 305, 405, 475, 505, 535));
        texts.AddRange(
        [
            new PdfText(40, 680, "02 Feb 2025"),
            new PdfText(100, 680, "Second row"),
            new PdfText(300, 680, "20.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.True(row.IsValid));
    }

    [Fact]
    public void HeaderResidueCannotBeConsumedAsACompleteHeader()
    {
        var texts = Header(800, 40, 100, 300, 400, 470, 500, 530);
        texts.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "First row"),
            new PdfText(300, 760, "10.00"),
        ]);
        texts.AddRange(Header(700, 40, 100, 300, 400, 470, 500, 530));
        texts.Add(new PdfText(560, 700, "RESIDUE"));
        texts.AddRange(
        [
            new PdfText(40, 680, "02 Feb 2025"),
            new PdfText(100, 680, "Second row"),
            new PdfText(300, 680, "20.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Contains(
            result.Rows,
            row => row.ValidationCodes.Contains(ValidationCodes.InvalidDate) &&
                   row.RawValues["description"] == "Description");
    }

    [Fact]
    public void AboveHeaderCandidatesKeepDistinctDocumentSourceLines()
    {
        var texts = new List<PdfText>
        {
            new(40, 820, "01 Feb 2025"),
            new(100, 820, "First above"),
            new(300, 820, "10.00"),
            new(40, 810, "02 Feb 2025"),
            new(100, 810, "Second above"),
            new(300, 810, "20.00"),
        };
        texts.AddRange(Header(780, 40, 100, 300, 400, 470, 500, 530));
        texts.AddRange(
        [
            new PdfText(40, 760, "03 Feb 2025"),
            new PdfText(100, 760, "Body row"),
            new PdfText(300, 760, "30.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal([1, 2, 4], result.Rows.Select(row => row.SourceLine).ToArray());
        Assert.Equal([1, 2, 3], result.Rows.Select(row => row.SourceRowNumber).ToArray());
        Assert.All(result.Rows, row => Assert.Equal(1, row.SourcePage));
    }

    [Fact]
    public void LaterPageSourceLinesIncludeAllConsumedLines()
    {
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(
        [
            new PdfText(40, 820, "Synthetic Bank"),
            new PdfText(40, 800, "Statement period: 01 Feb 2025 to 28 Feb 2025"),
        ]);

        var second = Header(800, 40, 100, 300, 400, 470, 500, 530);
        second.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "Second page row"),
            new PdfText(300, 760, "10.00"),
            new PdfText(100, 30, "Page 2 of 3"),
        ]);
        pdf.AddPage(second);

        var third = Header(800, 40, 100, 300, 400, 470, 500, 530);
        third.AddRange(
        [
            new PdfText(40, 760, "02 Feb 2025"),
            new PdfText(100, 760, "Third page row"),
            new PdfText(300, 760, "20.00"),
            new PdfText(100, 30, "Page 3 of 3"),
        ]);
        pdf.AddPage(third);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal([4, 7], result.Rows.Select(row => row.SourceLine).ToArray());
        Assert.Equal([2, 3], result.Rows.Select(row => row.SourcePage).ToArray());
    }

    [Fact]
    public void FourDigitDateBoundariesRemainInvalidRows()
    {
        var texts = Header(800, 40, 100, 300, 400, 470, 500, 530);
        var dates = new[]
        {
            "00 Feb 2025",
            "01 Feb 2025",
            "28 Feb 2025",
            "29 Feb 2025",
            "31 Apr 2025",
            "29 Feb 2024",
        };
        for (var index = 0; index < dates.Length; index++)
        {
            var y = 760 - index * 18;
            texts.Add(new PdfText(40, y, dates[index]));
            texts.Add(new PdfText(100, y, $"Boundary {index}"));
            texts.Add(new PdfText(300, y, $"{index + 1}.00"));
        }
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(pdf.Build(), null, null, null);

        Assert.Equal(6, result.Rows.Count);
        Assert.Contains("invalid_date", result.Rows[0].ValidationCodes);
        Assert.True(result.Rows[1].IsValid);
        Assert.True(result.Rows[2].IsValid);
        Assert.Contains("invalid_date", result.Rows[3].ValidationCodes);
        Assert.Contains("invalid_date", result.Rows[4].ValidationCodes);
        Assert.True(result.Rows[5].IsValid);
        Assert.Equal(-100, result.Rows[0].AmountCents);
        Assert.Equal(-200, result.Rows[1].AmountCents);
    }

    [Fact]
    public void TwoDigitDateBoundariesResolveAgainstTheConfirmedPeriod()
    {
        var texts = Header(800, 40, 100, 300, 400, 470, 500, 530);
        texts.AddRange(
        [
            new PdfText(40, 760, "00 Feb 24"),
            new PdfText(100, 760, "Zero day"),
            new PdfText(300, 760, "10.00"),
            new PdfText(40, 742, "29 Feb 24"),
            new PdfText(100, 742, "Leap day"),
            new PdfText(300, 742, "20.00"),
            new PdfText(40, 724, "28 Feb 24"),
            new PdfText(100, 724, "Last day"),
            new PdfText(300, 724, "30.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29), null);

        Assert.Equal(3, result.Rows.Count);
        Assert.Contains("invalid_date", result.Rows[0].ValidationCodes);
        Assert.Equal(new DateOnly(2024, 2, 29), result.Rows[1].TransactionDate);
        Assert.Equal(new DateOnly(2024, 2, 28), result.Rows[2].TransactionDate);
    }

    [Fact]
    public void TerminalTotalBeforeAHeaderlessSummaryRemainsAnInvalidCandidate()
    {
        var first = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage([new PdfText(40, 780, "Summary")]);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(4, result.Rows.Count);
        var total = Assert.Single(result.Rows, row => row.RawValues["description"] == "Total payments");
        Assert.Contains("invalid_date", total.ValidationCodes);
        Assert.Equal(-123456, total.AmountCents);
    }

    [Fact]
    public void TerminalTotalWithClosingBalanceAndFooterIsExcluded()
    {
        var texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
            new(100, 688, "Closing balance"),
            new(530, 688, "1,234.56"),
            new(100, 30, "Page 1 of 1"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
    }

    [Theory]
    [InlineData("Summary")]
    [InlineData("Synthetic Bank")]
    [InlineData("Statement period: 01 Jan 2025 to 31 Jan 2025")]
    public void TerminalTotalBeforeLaterRecognizedContentIsRetained(string content)
    {
        var first = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage([new PdfText(40, 780, content)]);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(4, result.Rows.Count);
        var total = Assert.Single(result.Rows, row => row.RawValues["description"] == "Total payments");
        Assert.Contains("invalid_date", total.ValidationCodes);
    }

    [Fact]
    public void TerminalTotalBeforeAHeaderOnlyRestartIsRetained()
    {
        var first = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        var second = Header(800, 40, 100, 300, 400, 470, 500, 530);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(4, result.Rows.Count);
        var total = Assert.Single(result.Rows, row => row.RawValues["description"] == "Total payments");
        Assert.Contains("invalid_date", total.ValidationCodes);
    }

    [Fact]
    public void TopOfPageSummaryJustInsideTheToleranceIsAccepted()
    {
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(StatementFixtureBuilder.HandAuthoredSmallPdfTexts());
        pdf.AddPage([new PdfText(40, 730, "Summary")]);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void TopOfPageSummaryAfterATableIsAccepted()
    {
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(StatementFixtureBuilder.HandAuthoredSmallPdfTexts());
        pdf.AddPage([new PdfText(40, 780, "Summary")]);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public void AdjacentCrossPageContinuationsAttachExactlyOnce()
    {
        var first = new List<PdfText>();
        first.AddRange(Header(800, 40, 100, 300, 400, 470, 500, 530));
        first.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "First row"),
            new PdfText(300, 760, "10.00"),
        ]);

        var second = new List<PdfText>();
        second.AddRange(Header(800, 40, 100, 300, 400, 470, 500, 530));
        second.AddRange(
        [
            new PdfText(100, 770, "first fragment"),
            new PdfText(100, 762, "second fragment"),
            new PdfText(40, 752, "02 Feb 2025"),
            new PdfText(100, 752, "Second row"),
            new PdfText(300, 752, "20.00"),
        ]);

        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("First row first fragment second fragment", result.Rows[0].Description);
        Assert.Equal("Second row", result.Rows[1].Description);
    }

    [Fact]
    public void FooterDoesNotBlockTheImmediateNextPageBridge()
    {
        var first = new List<PdfText>();
        first.AddRange(Header(800, 40, 100, 300, 400, 470, 500, 530));
        first.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "First row"),
            new PdfText(300, 760, "10.00"),
            new PdfText(100, 30, "Page 1 of 2"),
        ]);

        var second = new List<PdfText>();
        second.AddRange(Header(800, 40, 100, 300, 400, 470, 500, 530));
        second.AddRange(
        [
            new PdfText(100, 770, "continued after footer"),
            new PdfText(40, 752, "02 Feb 2025"),
            new PdfText(100, 752, "Second row"),
            new PdfText(300, 752, "20.00"),
            new PdfText(100, 30, "Page 2 of 2"),
        ]);

        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("First row continued after footer", result.Rows[0].Description);
    }

    [Fact]
    public void DatedAccountFeeDescriptionRemainsATransaction()
    {
        var texts = Header(800, 40, 100, 300, 400, 470, 500, 530);
        texts.AddRange(
        [
            new PdfText(40, 760, "01 Feb 2025"),
            new PdfText(100, 760, "Account fee"),
            new PdfText(300, 760, "10.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);

        var result = StandardBankPdfParser.Parse(
            pdf.Build(), new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28), null);

        Assert.Single(result.Rows);
        Assert.True(result.Rows[0].IsValid);
        Assert.Equal("Account fee", result.Rows[0].Description);
    }

    [Fact]
    public void FixtureContinuationCountsAreExact()
    {
        var fourPage = StatementFixtureBuilder.BuildStandardBankPdf(4, 49, 66);
        var eighteenPage = StatementFixtureBuilder.BuildStandardBankPdf(18, 297, 356);

        Assert.Equal(66, fourPage.Rows.Sum(row => CountOccurrences(row.Description, "continued note")));
        Assert.Equal(356, eighteenPage.Rows.Sum(row => CountOccurrences(row.Description, "continued note")));
    }

    private static List<PdfText> Header(double y, params double[] xs)
    {
        var labels = new[]
        {
            "Date", "Description", "Payments", "Deposits", "Bank", "fees", "Balance",
        };
        Assert.Equal(labels.Length, xs.Length);
        return labels.Select((label, index) => new PdfText(xs[index], y, label)).ToList();
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
