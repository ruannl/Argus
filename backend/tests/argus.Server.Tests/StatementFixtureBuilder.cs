using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace argus.Server.Tests;

public sealed record StatementFixtureRow(
    string Date,
    string Description,
    long? Cents,
    string[] ValidationCodes,
    int? Page);

public sealed record StatementFixture(
    byte[] Bytes,
    IReadOnlyList<StatementFixtureRow> Rows,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int ContinuationCount);

/// <summary>
/// Synthetic statement fixtures. No real bank data is present; every value is
/// generated from the structural contract in docs/product-brief.md.
/// </summary>
public static class StatementFixtureBuilder
{
    public static StatementFixture BuildFnbCsv(int rowCount, bool chequeAccount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Synthetic FNB export");
        builder.AppendLine("Account: SYNTHETIC-0000");
        builder.AppendLine("Statement: synthetic statement");
        builder.AppendLine("Currency: ZAR");
        builder.AppendLine("Generated: 2026-01-01");
        builder.AppendLine("Do not use with real data");
        builder.AppendLine("Date,Amount,Balance,Description");
        var rows = new List<StatementFixtureRow>(rowCount);
        var runningBalance = 100_000_00L;
        for (var index = 0; index < rowCount; index++)
        {
            var date = new DateOnly(2024, 1, 1).AddDays(index % 31);
            var cents = -(10_000 + index * 137);
            runningBalance += cents;
            var description = $"Synthetic merchant {index:D4}";
            if (chequeAccount)
            {
                description += " note";
            }
            else
            {
                description = description.PadRight(41);
            }
            var amount = (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var balance = (runningBalance / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            builder.Append(date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
                .Append(',').Append(amount)
                .Append(',').Append(chequeAccount ? balance : "0.00")
                .Append(',').Append(description)
                .Append('\n');
            rows.Add(new StatementFixtureRow(
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                description.Trim(),
                cents,
                [],
                null));
        }

        return new StatementFixture(
            Encoding.UTF8.GetBytes(builder.ToString()),
            rows,
            null,
            null,
            0);
    }

    public static StatementFixture BuildFnbZip(int rowCount, bool chequeAccount)
    {
        var csv = BuildFnbCsv(rowCount, chequeAccount);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("statement.csv", CompressionLevel.Fastest);
            using var stream = entry.Open();
            stream.Write(csv.Bytes);
        }
        return csv with { Bytes = output.ToArray() };
    }

    public static StatementFixture BuildStandardBankPdf(
        int pageCount,
        int rowCount,
        int continuationCount,
        bool includePeriod = true,
        bool crossYear = false,
        bool reorderedStreams = true)
    {
        var periodStart = crossYear ? new DateOnly(2023, 12, 15) : new DateOnly(2025, 1, 1);
        var periodEnd = crossYear ? new DateOnly(2024, 1, 15) : new DateOnly(2025, 1, 31);
        var tablePages = pageCount - 1;
        var rowsPerPage = DistributeRows(rowCount, tablePages);
        var continuationsPerRow = DistributeContinuations(rowCount, continuationCount);
        var rows = new List<StatementFixtureRow>(rowCount);
        var pdf = new MinimalPdfBuilder();
        var pageTexts = new List<List<PdfText>>();
        var random = new Random(20261104);

        // Cover page.
        var cover = new List<PdfText>
        {
            new(40, 820, "Synthetic Bank"),
            new(40, 800, "Synthetic statement"),
        };
        if (includePeriod)
        {
            cover.Add(new PdfText(40, 780,
                $"Statement period: {periodStart.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} to {periodEnd.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"));
        }
        pageTexts.Add(cover);

        var rowIndex = 0;
        var pageNumber = 0;
        List<string>? pendingContinuations = null;
        foreach (var count in rowsPerPage)
        {
            pageNumber++;
            var texts = new List<PdfText>();
            AddHeader(texts);
            var y = 770.0;
            if (pendingContinuations is { Count: > 0 })
            {
                foreach (var continuation in pendingContinuations)
                {
                    texts.Add(new PdfText(100, y, continuation));
                    y -= 8;
                }
                y -= 8;
                pendingContinuations = null;
            }

            for (var index = 0; index < count && rowIndex < rowCount; index++, rowIndex++)
            {
                var continuationLines = continuationsPerRow[rowIndex];
                var isLastOnPage = index == count - 1 && rowIndex != rowCount - 1;
                var date = periodStart.AddDays(rowIndex % (periodEnd.DayNumber - periodStart.DayNumber + 1));
                var amountCase = FlowCase(rowIndex);
                var cents = ExpectedCents(rowIndex, amountCase);
                var amountText = cents is null
                    ? "10.00"
                    : (Math.Abs(cents.Value) / 100m).ToString("N2", CultureInfo.InvariantCulture);
                var description = $"Synthetic merchant {rowIndex:D4}";
                var expectedDescription = description;
                texts.Add(new PdfText(40, y, date.ToString("dd MMM yy", CultureInfo.InvariantCulture)));
                texts.Add(new PdfText(100, y, description));
                texts.Add(new PdfText(530, y, "0.00"));

                switch (amountCase)
                {
                    case PdfFlowCase.Payments:
                        texts.Add(new PdfText(300, y, amountText!));
                        break;
                    case PdfFlowCase.Deposits:
                        texts.Add(new PdfText(400, y, amountText!));
                        break;
                    case PdfFlowCase.BankFees:
                        texts.Add(new PdfText(470, y, amountText!));
                        break;
                    case PdfFlowCase.Missing:
                        break;
                    case PdfFlowCase.Multiple:
                        texts.Add(new PdfText(300, y, amountText!));
                        texts.Add(new PdfText(400, y, "1.00"));
                        break;
                    case PdfFlowCase.ConflictingSign:
                        texts.Add(new PdfText(400, y, $"-{amountText!}"));
                        break;
                }

                var continuationTexts = new List<string>();
                for (var continuation = 0; continuation < continuationLines; continuation++)
                {
                    continuationTexts.Add($"continued note {rowIndex:D4}-{continuation}");
                }
                expectedDescription += continuationTexts.Count == 0
                    ? string.Empty
                    : " " + string.Join(" ", continuationTexts);

                if (isLastOnPage && continuationTexts.Count > 0)
                {
                    pendingContinuations = continuationTexts;
                    y -= 16;
                }
                else
                {
                    var continuationY = y;
                    foreach (var continuation in continuationTexts)
                    {
                        continuationY -= 8;
                        texts.Add(new PdfText(100, continuationY, continuation));
                    }
                    y = continuationY - 8;
                }

                rows.Add(new StatementFixtureRow(
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    expectedDescription,
                    cents,
                    ValidationFor(amountCase),
                    pageNumber + 1));
            }

            pageTexts.Add(texts);
        }

        foreach (var texts in pageTexts)
        {
            var ordered = reorderedStreams ? Shuffle(texts, random) : texts;
            pdf.AddPage(ordered);
        }

        return new StatementFixture(
            pdf.Build(),
            rows,
            includePeriod ? periodStart : null,
            includePeriod ? periodEnd : null,
            continuationCount);
    }

    /// <summary>
    /// Two-page PDF used by the PDF classification regressions. The first page
    /// is text; the second is either text or a full-page image. It never
    /// contains real account data.
    /// </summary>
    public static byte[] BuildTwoPagePdf(
        IReadOnlyList<PdfText> firstPage,
        IReadOnlyList<PdfText>? secondPage,
        bool secondPageIsImage = false,
        bool secondPageHasImage = false)
    {
        var pageCount = secondPage is not null || secondPageIsImage ? 2 : 1;
        var hasImage = secondPageIsImage || secondPageHasImage;
        var objectCount = 3 + pageCount * 2 + (hasImage ? 1 : 0);
        var offsets = new long[objectCount + 1];
        using var output = new MemoryStream();
        void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
        void BeginObject(int id)
        {
            offsets[id] = output.Position;
            Write($"{id} 0 obj\n");
        }
        void EndObject() => Write("endobj\n");
        string TextContent(IReadOnlyList<PdfText> texts)
        {
            var builder = new StringBuilder();
            foreach (var text in texts)
            {
                builder.Append("BT /F1 8 Tf 1 0 0 1 ")
                    .Append(text.X.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(text.Y.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append(" Tm (")
                    .Append(MinimalPdfBuilder.Escape(text.Text))
                    .Append(") Tj ET\n");
            }
            return builder.ToString();
        }

        Write("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObject();
        BeginObject(2);
        var kids = pageCount == 1 ? "4 0 R" : "4 0 R 6 0 R";
        Write($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\n");
        EndObject();
        BeginObject(3);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\n");
        EndObject();

        var firstContent = TextContent(firstPage);
        BeginObject(4);
        Write("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
              "/Resources << /Font << /F1 3 0 R >> >> /Contents 5 0 R >>\n");
        EndObject();
        BeginObject(5);
        Write($"<< /Length {Encoding.ASCII.GetByteCount(firstContent)} >>\nstream\n");
        Write(firstContent);
        Write("endstream\n");
        EndObject();

        if (pageCount == 2)
        {
            if (hasImage)
            {
                const string imageContent = "q 595 0 0 842 0 0 cm /Im1 Do Q\n";
                var secondContent = secondPageIsImage
                    ? imageContent
                    : TextContent(secondPage ?? []) + imageContent;
                var resources = secondPageIsImage
                    ? "/XObject << /Im1 8 0 R >>"
                    : "/Font << /F1 3 0 R >> /XObject << /Im1 8 0 R >>";
                BeginObject(6);
                Write("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
                      $"/Resources << {resources} >> /Contents 7 0 R >>\n");
                EndObject();
                BeginObject(7);
                Write($"<< /Length {Encoding.ASCII.GetByteCount(secondContent)} >>\nstream\n");
                Write(secondContent);
                Write("endstream\n");
                EndObject();
                BeginObject(8);
                Write("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
                      "/ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\n");
                output.WriteByte(0);
                Write("\nendstream\n");
                EndObject();
            }
            else
            {
                var secondContent = TextContent(secondPage ?? []);
                BeginObject(6);
                Write("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] " +
                      "/Resources << /Font << /F1 3 0 R >> >> /Contents 7 0 R >>\n");
                EndObject();
                BeginObject(7);
                Write($"<< /Length {Encoding.ASCII.GetByteCount(secondContent)} >>\nstream\n");
                Write(secondContent);
                Write("endstream\n");
                EndObject();
            }
        }

        var xref = output.Position;
        Write($"xref\n0 {objectCount + 1}\n");
        Write("0000000000 65535 f \n");
        for (var id = 1; id <= objectCount; id++)
        {
            Write($"{offsets[id]:D10} 00000 n \n");
        }
        Write($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    public static List<PdfText> HandAuthoredSmallPdfTexts() =>
    [
            new PdfText(40, 820, "Synthetic Bank"),
            new PdfText(40, 800, "Statement period: 01 Feb 2025 to 28 Feb 2025"),
            new PdfText(40, 780, "Date"),
            new PdfText(100, 780, "Description"),
            new PdfText(300, 780, "Payments"),
            new PdfText(400, 780, "Deposits"),
            new PdfText(470, 780, "Bank"),
            new PdfText(500, 780, "fees"),
            new PdfText(530, 780, "Balance"),
            new PdfText(40, 760, "01 Feb 25"),
            new PdfText(100, 760, "Hand written one"),
            new PdfText(300, 760, "10.00"),
            new PdfText(530, 760, "10.00"),
            new PdfText(40, 742, "02 Feb 25"),
            new PdfText(100, 742, "Hand written two"),
            new PdfText(400, 742, "20.00"),
            new PdfText(530, 742, "30.00"),
            new PdfText(40, 724, "03 Feb 25"),
            new PdfText(100, 724, "Hand written three"),
            new PdfText(470, 724, "1.50"),
            new PdfText(530, 724, "28.50"),
    ];

    public static byte[] BuildHandAuthoredSmallPdf()
    {
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(HandAuthoredSmallPdfTexts());
        return pdf.Build();
    }

    private static void AddHeader(List<PdfText> texts)
    {
        texts.Add(new PdfText(40, 800, "Date"));
        texts.Add(new PdfText(100, 800, "Description"));
        texts.Add(new PdfText(300, 800, "Payments"));
        texts.Add(new PdfText(400, 800, "Deposits"));
        texts.Add(new PdfText(470, 800, "Bank"));
        texts.Add(new PdfText(500, 800, "fees"));
        texts.Add(new PdfText(530, 800, "Balance"));
    }

    private static int[] DistributeRows(int rowCount, int pages)
    {
        var result = new int[pages];
        var baseCount = rowCount / pages;
        var remainder = rowCount % pages;
        for (var index = 0; index < pages; index++)
        {
            result[index] = baseCount + (index < remainder ? 1 : 0);
        }
        return result;
    }

    private static int[] DistributeContinuations(int rowCount, int total)
    {
        var result = new int[rowCount];
        if (total <= rowCount)
        {
            for (var index = 0; index < total; index++)
            {
                result[index] = 1;
            }
        }
        else
        {
            for (var index = 0; index < rowCount; index++)
            {
                result[index] = 1;
            }
            var remaining = total - rowCount;
            for (var index = 0; index < remaining; index++)
            {
                result[index % rowCount]++;
            }
        }
        return result;
    }

    private static PdfFlowCase FlowCase(int index)
    {
        if (index % 19 == 10)
        {
            return PdfFlowCase.Missing;
        }
        if (index % 23 == 11)
        {
            return PdfFlowCase.Multiple;
        }
        if (index % 29 == 12)
        {
            return PdfFlowCase.ConflictingSign;
        }
        return (index % 3) switch
        {
            0 => PdfFlowCase.Payments,
            1 => PdfFlowCase.Deposits,
            _ => PdfFlowCase.BankFees,
        };
    }

    private static long? ExpectedCents(int index, PdfFlowCase flowCase)
    {
        if (flowCase is PdfFlowCase.Missing or PdfFlowCase.Multiple or PdfFlowCase.ConflictingSign)
        {
            return null;
        }
        return flowCase switch
        {
            PdfFlowCase.Payments => -(10_000 + index * 137),
            PdfFlowCase.Deposits => 20_000 + index * 211,
            _ => -(500 + index * 7),
        };
    }

    private static string[] ValidationFor(PdfFlowCase flowCase) => flowCase switch
    {
        PdfFlowCase.Missing => ["missing_amount"],
        PdfFlowCase.Multiple => ["multiple_amounts"],
        PdfFlowCase.ConflictingSign => ["invalid_sign"],
        _ => [],
    };

    private static List<PdfText> Shuffle(List<PdfText> texts, Random random)
    {
        var copy = new List<PdfText>(texts);
        for (var index = copy.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }
        return copy;
    }

    private enum PdfFlowCase
    {
        Payments,
        Deposits,
        BankFees,
        Missing,
        Multiple,
        ConflictingSign,
    }
}

public readonly record struct PdfText(double X, double Y, string Text);

public sealed class MinimalPdfBuilder
{
    private readonly List<PageContent> _pages = [];

    public void AddPage(IEnumerable<PdfText> texts) => AddPage(texts, 0, 0, 0);

    public void AddPage(
        IEnumerable<PdfText> texts,
        int imageCount,
        double imageWidth,
        double imageHeight)
    {
        var builder = new StringBuilder();
        foreach (var text in texts)
        {
            builder.Append("BT /F1 8 Tf 1 0 0 1 ")
                .Append(text.X.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(text.Y.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" Tm (")
                .Append(Escape(text.Text))
                .Append(") Tj ET\n");
        }

        _pages.Add(new PageContent(builder.ToString(), imageCount, imageWidth, imageHeight));
    }

    public byte[] Build()
    {
        var totalImages = _pages.Sum(page => page.ImageCount);
        var objectCount = 3 + _pages.Count * 2 + totalImages;
        var offsets = new long[objectCount + 1];
        var imageIds = new int[_pages.Count][];
        var nextImageId = 4 + _pages.Count * 2;
        for (var index = 0; index < _pages.Count; index++)
        {
            imageIds[index] = Enumerable.Range(0, _pages[index].ImageCount)
                .Select(_ => nextImageId++)
                .ToArray();
        }

        using var output = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            output.Write(bytes);
        }
        void BeginObject(int id)
        {
            offsets[id] = output.Position;
            Write($"{id} 0 obj\n");
        }
        void EndObject() => Write("endobj\n");

        Write("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObject();
        BeginObject(2);
        var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(index => $"{4 + index * 2} 0 R"));
        Write($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>\n");
        EndObject();
        BeginObject(3);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\n");
        EndObject();

        for (var index = 0; index < _pages.Count; index++)
        {
            var page = _pages[index];
            var pageId = 4 + index * 2;
            var contentId = pageId + 1;
            var resources = "/Font << /F1 3 0 R >>";
            if (page.ImageCount > 0)
            {
                var xobjects = string.Join(" ", imageIds[index]
                    .Select((id, imageIndex) => $"/Im{imageIndex + 1} {id} 0 R"));
                resources += $" /XObject << {xobjects} >>";
            }
            BeginObject(pageId);
            Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << {resources} >> /Contents {contentId} 0 R >>\n");
            EndObject();
            var content = page.Text + ImageContent(page);
            BeginObject(contentId);
            Write($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n");
            Write(content);
            Write("endstream\n");
            EndObject();
        }

        for (var index = 0; index < _pages.Count; index++)
        {
            for (var imageIndex = 0; imageIndex < imageIds[index].Length; imageIndex++)
            {
                BeginObject(imageIds[index][imageIndex]);
                Write("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
                      "/ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\n");
                output.WriteByte(0);
                Write("\nendstream\n");
                EndObject();
            }
        }

        var xref = output.Position;
        Write($"xref\n0 {objectCount + 1}\n");
        Write("0000000000 65535 f \n");
        for (var id = 1; id <= objectCount; id++)
        {
            Write($"{offsets[id]:D10} 00000 n \n");
        }
        Write($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    public byte[] BuildImageOnly(
        int imageCount = 1,
        double imageWidth = 100,
        double imageHeight = 100)
    {
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage([], imageCount, imageWidth, imageHeight);
        return pdf.Build();
    }

    internal static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string ImageContent(PageContent page)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < page.ImageCount; index++)
        {
            builder.Append("q ")
                .Append(page.ImageWidth.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" 0 0 ")
                .Append(page.ImageHeight.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" 100 ")
                .Append((100 + index * 10).ToString(CultureInfo.InvariantCulture))
                .Append(" cm /Im")
                .Append(index + 1)
                .Append(" Do Q\n");
        }
        return builder.ToString();
    }

    private sealed record PageContent(
        string Text,
        int ImageCount,
        double ImageWidth,
        double ImageHeight);
}
