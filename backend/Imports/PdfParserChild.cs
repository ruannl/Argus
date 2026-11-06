using System.Buffers.Binary;
using System.Text.Json;

namespace argus.Server.Imports;

/// <summary>
/// Early child entry point for PDF extraction. It reads a bounded binary frame
/// from standard input, never loads configuration or web services, and writes
/// only bounded sanitized JSON to standard output.
/// </summary>
public static class PdfParserChild
{
    private const int HeaderLengthBytes = 4;
    private const int MaxHeaderBytes = 64 * 1024;

    public static async Task<int> RunAsync(
        Stream input,
        Stream output,
        Stream error,
        TimeSpan? hardDeadline = null,
        Func<byte[], PdfChildRequestOptions, ParsedStatement>? parser = null)
    {
        try
        {
            var work = Task.Run(() => ReadAndParseAsync(input, parser ?? ((bytes, options) =>
                StandardBankPdfParser.Parse(bytes, options.StartDate, options.EndDate, options.Century))));
            var deadline = hardDeadline ?? TimeSpan.FromSeconds(ImportLimits.ParserDeadlineSeconds);
            var completed = await Task.WhenAny(work, Task.Delay(deadline));
            if (completed != work)
            {
                await WriteAsync(output, new PdfChildResponse(false, ParserReasons.ProcessingLimit, null));
                return 0;
            }

            var response = await work;
            await WriteAsync(output, response);
            return 0;
        }
        catch (StatementRejectedException exception)
        {
            await TryWriteAsync(output, new PdfChildResponse(false, exception.Reason, null));
            return 0;
        }
        catch (Exception)
        {
            await TryWriteAsync(output, new PdfChildResponse(false, ParserReasons.UnsupportedLayout, null));
            return 0;
        }
        finally
        {
            // The child never writes diagnostics; the parameter remains for the
            // production entry point's symmetry with stdout.
            _ = error;
        }
    }

    private static async Task<PdfChildResponse> ReadAndParseAsync(
        Stream input,
        Func<byte[], PdfChildRequestOptions, ParsedStatement> parser)
    {
        var headerLengthBytes = new byte[HeaderLengthBytes];
        if (!await ReadExactAsync(input, headerLengthBytes))
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }
        var headerLength = BinaryPrimitives.ReadInt32BigEndian(headerLengthBytes);
        if (headerLength is <= 0 or > MaxHeaderBytes)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        var headerBytes = new byte[headerLength];
        if (!await ReadExactAsync(input, headerBytes))
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        PdfChildRequestOptions options;
        try
        {
            options = JsonSerializer.Deserialize<PdfChildRequestOptions>(
                headerBytes, PdfChildProtocol.JsonOptions)
                ?? throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }
        catch (JsonException)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        var fileBytes = new List<byte>(Math.Min(ImportLimits.MaxFileBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            if (fileBytes.Count + read > ImportLimits.MaxFileBytes)
            {
                throw new StatementRejectedException(ParserReasons.FileTooLarge);
            }
            fileBytes.AddRange(buffer.AsSpan(0, read));
        }

        if (fileBytes.Count == 0)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        var statement = parser(fileBytes.ToArray(), options);
        return new PdfChildResponse(true, null, PdfChildProtocol.ToChild(statement));
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }

    private static async Task WriteAsync(Stream output, PdfChildResponse response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, PdfChildProtocol.JsonOptions);
        if (bytes.Length > ImportLimits.MaxPdfChildOutputBytes)
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(
                new PdfChildResponse(false, ParserReasons.ProcessingLimit, null),
                PdfChildProtocol.JsonOptions);
        }
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    }

    private static async Task TryWriteAsync(Stream output, PdfChildResponse response)
    {
        try
        {
            await WriteAsync(output, response);
        }
        catch
        {
            // The parent may already be gone; the process still exits on a bound.
        }
    }
}
