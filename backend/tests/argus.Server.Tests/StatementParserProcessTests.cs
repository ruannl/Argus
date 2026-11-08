using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using argus.Server.Imports;

namespace argus.Server.Tests;

public sealed class StatementParserProcessTests
{
    [Fact]
    public async Task ChildModeParsesPdfFromStandardInput()
    {
        var bytes = StatementFixtureBuilder.BuildHandAuthoredSmallPdf();
        using var runner = new PdfParserRunner(timeout: TimeSpan.FromSeconds(20));

        var result = await runner.ParseAsync(
            bytes,
            new PdfChildRequestOptions(
                new DateOnly(2025, 2, 1),
                new DateOnly(2025, 2, 28),
                null,
                20_000));

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(-1000, result.Rows[0].AmountCents);
        Assert.Equal(2000, result.Rows[1].AmountCents);
    }

    [Fact]
    public async Task TimeoutKillsAChildThatDoesNotReadOrExit()
    {
        using var runner = new PdfParserRunner(
            () => Shell("sleep 30"),
            TimeSpan.FromMilliseconds(500));

        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(
                new byte[512 * 1024],
                new PdfChildRequestOptions(null, null, null, 500)));

        Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
    }

    [Fact]
    public async Task StderrFloodDoesNotDeadlockOrLeak()
    {
        using var runner = new PdfParserRunner(
            () => Shell("head -c 200000 /dev/zero 1>&2; exit 3"),
            TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(
                StatementFixtureBuilder.BuildHandAuthoredSmallPdf(),
                new PdfChildRequestOptions(null, null, null, 5_000)));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public async Task AbnormalExitIsASafeFailure()
    {
        using var runner = new PdfParserRunner(
            () => Shell("exit 7"),
            TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(
                StatementFixtureBuilder.BuildHandAuthoredSmallPdf(),
                new PdfChildRequestOptions(null, null, null, 5_000)));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public async Task NonZeroExitIsRejectedEvenWithSuccessShapedOutput()
    {
        const string json = "{\"ok\":true,\"statement\":{\"format\":\"standard_bank_pdf\",\"rows\":[],\"proposed_start_date\":null,\"proposed_end_date\":null,\"mapping_suggestions\":{},\"required_confirmations\":[],\"warnings\":[]}}";
        using var runner = new PdfParserRunner(
            () => Shell($"printf '%s' '{json}'; exit 7"),
            TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(
                new byte[] { 1 },
                new PdfChildRequestOptions(null, null, null, 5_000)));

        Assert.Equal(ParserReasons.UnsupportedLayout, exception.Reason);
    }

    [Fact]
    public async Task ChildWatchdogBoundsAStalledParser()
    {
        var stalled = new TaskCompletionSource<ParsedStatement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var input = new MemoryStream(BuildFrame(new byte[] { 1, 2, 3 }));
        await using var output = new MemoryStream();

        var run = PdfParserChild.RunAsync(
            input,
            output,
            Stream.Null,
            TimeSpan.FromMilliseconds(200),
            (_, _) => stalled.Task.GetAwaiter().GetResult());

        var exitCode = await run;
        Assert.Equal(0, exitCode);
        output.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<PdfChildResponse>(
            output, PdfChildProtocol.JsonOptions);
        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal(ParserReasons.ProcessingLimit, response.Reason);
        stalled.SetResult(new ParsedStatement(
            StatementFormat.StandardBankPdf, [], null, null,
            new Dictionary<string, string>(), [], []));
    }

    [Fact]
    public async Task TruncatedInputIsRejected()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();

        var exitCode = await PdfParserChild.RunAsync(input, output, Stream.Null);

        Assert.Equal(0, exitCode);
        output.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<PdfChildResponse>(
            output, PdfChildProtocol.JsonOptions);
        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal(ParserReasons.InvalidRequest, response.Reason);
    }

    [Fact]
    public async Task DescendantHoldingAnInheritedPipeDiesWithTheRootProcessGroup()
    {
        if (!OperatingSystem.IsLinux())
        {
            // Process groups are the deployment cleanup mechanism and Linux is
            // the supported backend runtime.
            return;
        }

        var pidFile = TempPath("descendant");
        try
        {
            using var runner = new PdfParserRunner(
                () => GroupLeaderStartInfo(
                    $"sleep 20 & echo $! > '{pidFile}'; exit 0"),
                TimeSpan.FromSeconds(2));

            var exception = await Assert.ThrowsAsync<StatementRejectedException>(() =>
                runner.ParseAsync(
                    new byte[] { 1 },
                    new PdfChildRequestOptions(null, null, null, 5_000)));

            Assert.Equal(ParserReasons.ProcessingLimit, exception.Reason);
            var descendantPid = await WaitForPidAsync(pidFile, TimeSpan.FromSeconds(5));
            Assert.True(
                await WaitForProcessExitAsync(descendantPid, TimeSpan.FromSeconds(3)),
                $"descendant {descendantPid} survived root exit and group cleanup");
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task ChildExitsWhenItsParentDiesAndTheInputPipeCloses()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fifo = TempPath("fifo");
        var pidFile = TempPath("child");
        using var helper = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        helper.StartInfo.ArgumentList.Add("-c");
        helper.StartInfo.ArgumentList.Add(
            $"mkfifo '{fifo}'; {ChildCommand()} < '{fifo}' & echo $! > '{pidFile}'; " +
            $"exec 3>'{fifo}'; printf '\\000\\000\\000\\020part' >&3; wait");

        try
        {
            Assert.True(helper.Start());
            var childPid = await WaitForPidAsync(pidFile, TimeSpan.FromSeconds(15));
            await Task.Delay(300);
            Assert.False(
                ProcessHasExited(childPid),
                $"parser child {childPid} exited before the parent was killed");
            helper.Kill(entireProcessTree: false);

            Assert.True(
                await WaitForProcessExitAsync(childPid, TimeSpan.FromSeconds(5)),
                $"parser child {childPid} survived parent death after input closure");
        }
        finally
        {
            if (!helper.HasExited)
            {
                helper.Kill(entireProcessTree: false);
            }
            helper.Dispose();
            File.Delete(fifo);
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task PublishedReleaseOutputLaunchesTheRealParserChild()
    {
        var directory = await _publishedServerDirectory.Value;
        using var runner = new PdfParserRunner(
            () => new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = directory,
                ArgumentList =
                {
                    Path.Combine(directory, "argus.Server.dll"),
                    "--parse-statement-pdf",
                },
            },
            TimeSpan.FromSeconds(20));

        var result = await runner.ParseAsync(
            StatementFixtureBuilder.BuildHandAuthoredSmallPdf(),
            new PdfChildRequestOptions(
                new DateOnly(2025, 2, 1),
                new DateOnly(2025, 2, 28),
                null,
                20_000));

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(-1000, result.Rows[0].AmountCents);

        var imagePage = StatementFixtureBuilder.BuildTwoPagePdf(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), null, secondPageIsImage: true);
        var headerless = new List<PdfText>
        {
            new(40, 760, "2025/01/02"),
            new(100, 760, "Hand written continuation"),
            new(300, 760, "-20.00"),
        };
        var headerlessPage = StatementFixtureBuilder.BuildTwoPagePdf(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), headerless);
        var missingDateTexts = new List<PdfText>(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "No date payment"),
            new(300, 706, "-20.00"),
        };
        var missingDateBuilder = new MinimalPdfBuilder();
        missingDateBuilder.AddPage(missingDateTexts);
        var missingDatePage = missingDateBuilder.Build();

        var options = new PdfChildRequestOptions(null, null, null, 20_000);
        var imageFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(imagePage, options));
        Assert.Equal(ParserReasons.TextPdfRequired, imageFailure.Reason);
        var headerlessFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(headerlessPage, options));
        Assert.Equal(ParserReasons.UnsupportedLayout, headerlessFailure.Reason);
        var missingDateResult = await runner.ParseAsync(missingDatePage, options);
        Assert.Equal(4, missingDateResult.Rows.Count);
        Assert.Contains(
            missingDateResult.Rows,
            row => row.ValidationCodes.Contains(ValidationCodes.InvalidDate) &&
                   row.RawValues["description"] == "No date payment");

        var headerTexts = new List<PdfText>
        {
            new(40, 780, "Date"),
            new(100, 780, "Description"),
            new(300, 780, "Payments"),
            new(400, 780, "Deposits"),
            new(470, 780, "Bank"),
            new(500, 780, "fees"),
            new(530, 780, "Balance"),
        };
        var headerWithImage = StatementFixtureBuilder.BuildTwoPagePdf(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts(),
            headerTexts,
            secondPageHasImage: true);
        var headerImageFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(headerWithImage, options));
        Assert.Equal(ParserReasons.TextPdfRequired, headerImageFailure.Reason);

        var badDate = StatementFixtureBuilder.BuildTwoPagePdf(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts(),
            [
                new(40, 760, "BAD-DATE"),
                new(100, 760, "Bank charge"),
                new(300, 760, "-20.00"),
            ]);
        var badDateFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(badDate, options));
        Assert.Equal(ParserReasons.UnsupportedLayout, badDateFailure.Reason);

        var integerAmount = StatementFixtureBuilder.BuildTwoPagePdf(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts(),
            [
                new(40, 760, "02 Jan 25"),
                new(100, 760, "Bank charge"),
                new(300, 760, "20"),
            ]);
        var integerFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(integerAmount, options));
        Assert.Equal(ParserReasons.UnsupportedLayout, integerFailure.Reason);

        var summaryCandidateTexts = new List<PdfText>(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Summary Books purchase"),
            new(300, 706, "-20.00"),
        };
        var summaryCandidateBuilder = new MinimalPdfBuilder();
        summaryCandidateBuilder.AddPage(summaryCandidateTexts);
        var summaryCandidate = await runner.ParseAsync(summaryCandidateBuilder.Build(), options);
        Assert.Equal(4, summaryCandidate.Rows.Count);
        Assert.Contains(
            summaryCandidate.Rows,
            row => row.ValidationCodes.Contains(ValidationCodes.InvalidDate) &&
                   row.RawValues["description"] == "Summary Books purchase");

        foreach (var amount in new[] { "-1,000.00", "-1000.00", "20" })
        {
            var periodPage = StatementFixtureBuilder.BuildTwoPagePdf(
                StatementFixtureBuilder.HandAuthoredSmallPdfTexts(),
                [
                    new(40, 780, "Statement period: 01 Jan 2025 to 31 Jan 2025"),
                    new(40, 760, "BAD-DATE"),
                    new(100, 760, "Bank charge"),
                    new(300, 760, amount),
                ]);
            var periodFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
                runner.ParseAsync(periodPage, options));
            Assert.Equal(ParserReasons.UnsupportedLayout, periodFailure.Reason);
        }

        var totalTexts = new List<PdfText>(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        var totalBuilder = new MinimalPdfBuilder();
        totalBuilder.AddPage(totalTexts);
        var totalResult = await runner.ParseAsync(totalBuilder.Build(), options);
        Assert.Equal(3, totalResult.Rows.Count);

        var residualTexts = new List<PdfText>(
            StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 600, "Residual after valid content"),
        };
        var residualBuilder = new MinimalPdfBuilder();
        residualBuilder.AddPage(residualTexts);
        var residualFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(residualBuilder.Build(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, residualFailure.Reason);

        var smallImage = new MinimalPdfBuilder();
        smallImage.AddPage(StatementFixtureBuilder.HandAuthoredSmallPdfTexts(), 1, 50, 50);
        var smallImageFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(smallImage.Build(), options));
        Assert.Equal(ParserReasons.TextPdfRequired, smallImageFailure.Reason);

        var compatibleRepeat = await runner.ParseAsync(BuildCompatibleRepeatedHeaderPdf(), options);
        Assert.Equal(2, compatibleRepeat.Rows.Count);
        Assert.All(compatibleRepeat.Rows, row => Assert.True(row.IsValid));

        var residueHeader = await runner.ParseAsync(BuildHeaderResiduePdf(), options);
        Assert.Equal(3, residueHeader.Rows.Count);
        Assert.Contains(
            residueHeader.Rows,
            row => row.ValidationCodes.Contains(ValidationCodes.InvalidDate));

        foreach (var conflicting in new[]
                 {
                     BuildConflictingHeaderDepositPdf(),
                     BuildConflictingHeaderBalancePdf(),
                     BuildToleratedShiftColumnChangePdf(),
                 })
        {
            var conflictFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
                runner.ParseAsync(conflicting, options));
            Assert.Equal(ParserReasons.UnsupportedLayout, conflictFailure.Reason);
        }

        var metadataFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(BuildMetadataPreamblePdf(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, metadataFailure.Reason);

        foreach (var negative in new[]
                 {
                     BuildNegativeXRowPdf(),
                     BuildNegativeXTotalPdf(),
                 })
        {
            var negativeFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
                runner.ParseAsync(negative, options));
            Assert.Equal(ParserReasons.UnsupportedLayout, negativeFailure.Reason);
        }

        var farContinuationFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(BuildFarContinuationPdf(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, farContinuationFailure.Reason);

        var distantFirstFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(BuildDistantFirstContinuationPdf(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, distantFirstFailure.Reason);

        var belowFooterFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(BuildBelowFooterContinuationPdf(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, belowFooterFailure.Reason);

        var summaryClosureFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(BuildSummaryClosedContinuationPdf(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, summaryClosureFailure.Reason);

        var midPageSummaryFailure = await Assert.ThrowsAsync<StatementRejectedException>(() =>
            runner.ParseAsync(BuildMidPageSummaryPdf(), options));
        Assert.Equal(ParserReasons.UnsupportedLayout, midPageSummaryFailure.Reason);

        var sourceLineResult = await runner.ParseAsync(BuildSourceLinePdf(), options);
        Assert.Equal(3, sourceLineResult.Rows.Count);
        Assert.Equal([1, 2, 4], sourceLineResult.Rows.Select(row => row.SourceLine).ToArray());
        Assert.Equal([1, 2, 3], sourceLineResult.Rows.Select(row => row.SourceRowNumber).ToArray());

        var zeroDayResult = await runner.ParseAsync(BuildZeroDayPdf(), options);
        Assert.Equal(6, zeroDayResult.Rows.Count);
        Assert.Contains("invalid_date", zeroDayResult.Rows[0].ValidationCodes);
        Assert.True(zeroDayResult.Rows[1].IsValid);
        Assert.True(zeroDayResult.Rows[2].IsValid);
        Assert.Contains("invalid_date", zeroDayResult.Rows[3].ValidationCodes);
        Assert.Contains("invalid_date", zeroDayResult.Rows[4].ValidationCodes);
        Assert.True(zeroDayResult.Rows[5].IsValid);

        var totalBeforeSummary = await runner.ParseAsync(BuildTotalBeforeSummaryPdf(), options);
        Assert.Equal(4, totalBeforeSummary.Rows.Count);
        Assert.Contains(
            "invalid_date",
            Assert.Single(
                totalBeforeSummary.Rows,
                row => row.RawValues["description"] == "Total payments").ValidationCodes);

        var fullSuffix = await runner.ParseAsync(BuildFullSuffixPdf(), options);
        Assert.Equal(3, fullSuffix.Rows.Count);
    }

    [Fact]
    public async Task PublishedChildDoesNotWriteSourceCanariesToStandardError()
    {
        var directory = await _publishedServerDirectory.Value;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = directory,
                ArgumentList =
                {
                    Path.Combine(directory, "argus.Server.dll"),
                    "--parse-statement-pdf",
                },
            },
        };

        Assert.True(process.Start());
        await process.StandardInput.BaseStream.WriteAsync(BuildFrame(BuildMetadataPreamblePdf()));
        process.StandardInput.Close();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.DoesNotContain("PRIVATE_ACCOUNT_7731", stderr, StringComparison.Ordinal);
        var response = JsonSerializer.Deserialize<PdfChildResponse>(
            stdout, PdfChildProtocol.JsonOptions);
        Assert.NotNull(response);
        Assert.False(response!.Ok);
        Assert.Equal(ParserReasons.UnsupportedLayout, response.Reason);
    }

    private static List<PdfText> ProcessHeader(double y,
                                               params double[] xs)
    {
        var labels = new[]
        {
            "Date", "Description", "Payments", "Deposits", "Bank", "fees", "Balance",
        };
        Assert.Equal(labels.Length, xs.Length);
        return labels.Select((label, index) => new PdfText(xs[index], y, label)).ToList();
    }

    private static void AddPaymentRow(List<PdfText> texts,
                                      double y,
                                      string date,
                                      string description,
                                      string payment) =>
        texts.AddRange(
        [
            new PdfText(40, y, date),
            new PdfText(100, y, description),
            new PdfText(300, y, payment),
        ]);

    private static byte[] BuildCompatibleRepeatedHeaderPdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(texts, 760, "01 Feb 2025", "First row", "10.00");
        texts.AddRange(ProcessHeader(700, 40, 100, 300, 400, 470, 500, 530));
        AddPaymentRow(texts, 680, "02 Feb 2025", "Second row", "20.00");
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildHeaderResiduePdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(texts, 760, "01 Feb 2025", "First row", "10.00");
        texts.AddRange(ProcessHeader(700, 40, 100, 300, 400, 470, 500, 530));
        texts.Add(new PdfText(560, 700, "RESIDUE"));
        AddPaymentRow(texts, 680, "02 Feb 2025", "Second row", "20.00");
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildConflictingHeaderDepositPdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(texts, 760, "01 Feb 2025", "First row", "10.00");
        texts.AddRange(ProcessHeader(700, 40, 100, 220, 300, 400, 430, 500));
        AddPaymentRow(texts, 680, "02 Feb 2025", "Intended deposit", "20.00");
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildConflictingHeaderBalancePdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(texts, 760, "01 Feb 2025", "First row", "10.00");
        texts.AddRange(ProcessHeader(700, 40, 100, 220, 300, 400, 430, 500));
        texts.AddRange(
        [
            new PdfText(40, 680, "02 Feb 2025"),
            new PdfText(100, 680, "Balance only"),
            new PdfText(500, 680, "9,999.99"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildMetadataPreamblePdf()
    {
        var texts = new List<PdfText>
        {
            new(40, 840, "Synthetic Bank"),
            new(40, 820, "Account: PRIVATE_ACCOUNT_7731"),
        };
        texts.AddRange(ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530));
        AddPaymentRow(texts, 760, "01 Feb 2025", "Valid row", "10.00");
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildNegativeXRowPdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(texts, 760, "01 Feb 2025", "Valid row", "10.00");
        texts.Add(new PdfText(-95, 760, "UNMAPPED_CANARY_4421"));
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildNegativeXTotalPdf()
    {
        var texts = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
            new(-95, 706, "UNMAPPED_CANARY_4421"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildFarContinuationPdf()
    {
        var first = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        var second = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        second.AddRange(
        [
            new PdfText(100, 770, "first fragment"),
            new PdfText(100, 300, "Unrelated far text"),
            new PdfText(40, 270, "02 Feb 2025"),
            new PdfText(100, 270, "Second row"),
            new PdfText(300, 270, "20.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);
        return pdf.Build();
    }

    private static byte[] BuildDistantFirstContinuationPdf()
    {
        var first = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        var second = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        second.AddRange(
        [
            new PdfText(100, 300, "distant fragment"),
            new PdfText(40, 270, "02 Feb 2025"),
            new PdfText(100, 270, "Second row"),
            new PdfText(300, 270, "20.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);
        return pdf.Build();
    }

    private static byte[] BuildSummaryClosedContinuationPdf()
    {
        var first = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        var second = new List<PdfText> { new(100, 820, "Summary") };
        second.AddRange(ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530));
        second.AddRange(
        [
            new PdfText(100, 770, "continued after summary"),
            new PdfText(40, 752, "02 Feb 2025"),
            new PdfText(100, 752, "Second row"),
            new PdfText(300, 752, "20.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);
        return pdf.Build();
    }

    private static byte[] BuildMidPageSummaryPdf()
    {
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(StatementFixtureBuilder.HandAuthoredSmallPdfTexts());
        pdf.AddPage([new PdfText(100, 400, "Summary")]);
        return pdf.Build();
    }

    private static byte[] BuildSourceLinePdf()
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
        texts.AddRange(ProcessHeader(780, 40, 100, 300, 400, 470, 500, 530));
        AddPaymentRow(texts, 760, "03 Feb 2025", "Body row", "30.00");
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildZeroDayPdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        var dates = new[]
        {
            "00 Feb 2025", "01 Feb 2025", "28 Feb 2025",
            "29 Feb 2025", "31 Apr 2025", "29 Feb 2024",
        };
        for (var index = 0; index < dates.Length; index++)
        {
            AddPaymentRow(
                texts, 760 - index * 18, dates[index], $"Boundary {index}", $"{index + 1}.00");
        }
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildBelowFooterContinuationPdf()
    {
        var first = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(first, 760, "01 Feb 2025", "First row", "10.00");
        var second = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        second.AddRange(
        [
            new PdfText(100, 770, "continued"),
            new PdfText(100, 30, "Page 2 of 2"),
            new PdfText(100, 20, "Residual below footer"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage(second);
        return pdf.Build();
    }

    private static byte[] BuildToleratedShiftColumnChangePdf()
    {
        var texts = ProcessHeader(800, 40, 100, 300, 400, 470, 500, 530);
        AddPaymentRow(texts, 760, "01 Feb 2025", "First row", "10.00");
        texts.AddRange(ProcessHeader(700, 40, 100, 300, 404, 470, 500, 530));
        texts.AddRange(
        [
            new PdfText(40, 680, "02 Feb 2025"),
            new PdfText(100, 680, "Shifted boundary"),
            new PdfText(359, 680, "10.00"),
        ]);
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(texts);
        return pdf.Build();
    }

    private static byte[] BuildFullSuffixPdf()
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
        return pdf.Build();
    }

    private static byte[] BuildTotalBeforeSummaryPdf()
    {
        var first = new List<PdfText>(StatementFixtureBuilder.HandAuthoredSmallPdfTexts())
        {
            new(100, 706, "Total payments"),
            new(300, 706, "1,234.56"),
        };
        var pdf = new MinimalPdfBuilder();
        pdf.AddPage(first);
        pdf.AddPage([new PdfText(40, 780, "Summary")]);
        return pdf.Build();
    }

    private static readonly Lazy<Task<string>> _publishedServerDirectory =
        new(PublishServerAsync);

    private static async Task<string> PublishServerAsync()
    {
        var project = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../argus.Server.csproj"));
        Assert.True(File.Exists(project), $"server project not found at {project}");
        var output = Path.Combine(
            Path.GetTempPath(), $"argus-published-{Guid.NewGuid():N}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(project)!,
            },
        };
        foreach (var argument in new[]
                 {
                     "publish", project, "-c", "Release", "-o", output, "--nologo",
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"dotnet publish failed with {process.ExitCode}:{Environment.NewLine}{stdout}{stderr}");
        return output;
    }

    private static ProcessStartInfo GroupLeaderStartInfo(string command) => new()
    {
        FileName = "/usr/bin/setsid",
        ArgumentList = { "/bin/sh", "-c", command },
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };

    [Fact]
    public async Task ProductionChildJoinsItsOwnProcessGroup()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var process = new Process { StartInfo = RealChildStartInfo() };
        Assert.True(process.Start());
        try
        {
            var groupId = await WaitForProcessGroupAsync(process.Id, TimeSpan.FromSeconds(5));
            Assert.Equal(process.Id, groupId);
        }
        finally
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<int> WaitForProcessGroupAsync(int pid,
                                                            TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = -1;
        while (DateTime.UtcNow < deadline)
        {
            var stat = await File.ReadAllTextAsync($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            last = int.Parse(fields[2]);
            if (last == pid)
            {
                return last;
            }
            await Task.Delay(25);
        }
        return last;
    }

    private static ProcessStartInfo RealChildStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        var serverAssembly = typeof(Program).Assembly.Location;
        var runtimeConfig = Path.ChangeExtension(serverAssembly, ".runtimeconfig.json");
        if (!File.Exists(runtimeConfig))
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            Assert.False(string.IsNullOrEmpty(entry));
            var deps = Path.ChangeExtension(entry, ".deps.json");
            var testRuntimeConfig = Path.ChangeExtension(entry, ".runtimeconfig.json");
            startInfo.ArgumentList.Add("exec");
            if (File.Exists(deps))
            {
                startInfo.ArgumentList.Add("--depsfile");
                startInfo.ArgumentList.Add(deps);
            }
            if (File.Exists(testRuntimeConfig))
            {
                startInfo.ArgumentList.Add("--runtimeconfig");
                startInfo.ArgumentList.Add(testRuntimeConfig);
            }
        }
        startInfo.ArgumentList.Add(serverAssembly);
        startInfo.ArgumentList.Add("--parse-statement-pdf");
        return startInfo;
    }

    private static string ChildCommand()
    {
        var startInfo = RealChildStartInfo();
        return string.Join(
            ' ',
            new[] { "dotnet" }.Concat(startInfo.ArgumentList).Select(ShellQuote));
    }

    private static string ShellQuote(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string TempPath(string suffix) =>
        Path.Combine(Path.GetTempPath(), $"argus-{suffix}-{Guid.NewGuid():N}.tmp");

    private static async Task<int> WaitForPidAsync(string path,
                                                   TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var text = await File.ReadAllTextAsync(path);
                if (int.TryParse(text.Trim(), out var pid) && pid > 0)
                {
                    return pid;
                }
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"PID file {path} was not written");
    }

    private static async Task<bool> WaitForProcessExitAsync(int pid,
                                                            TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ProcessHasExited(pid))
            {
                return true;
            }
            await Task.Delay(50);
        }
        return ProcessHasExited(pid);
    }

    private static bool ProcessHasExited(int pid)
    {
        if (!Directory.Exists($"/proc/{pid}"))
        {
            return true;
        }
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return fields.Length > 0 && fields[0] == "Z";
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static ProcessStartInfo Shell(string command) => new()
    {
        FileName = "/bin/sh",
        ArgumentList = { "-c", command },
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };

    private static byte[] BuildFrame(byte[] payload)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(
            new PdfChildRequestOptions(null, null, null, 30_000),
            PdfChildProtocol.JsonOptions);
        using var output = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, header.Length);
        output.Write(length);
        output.Write(header);
        output.Write(payload);
        return output.ToArray();
    }
}
