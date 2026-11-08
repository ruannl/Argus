using System.Text;
using argus.Server.Imports;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace argus.Server.Http;

public sealed record MultipartImport(byte[] FileBytes, string OptionsJson);

public static class MultipartImportReader
{
    public static async Task<MultipartImport> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) || !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase) || mediaType.Boundary.Length == 0)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }
        if (request.ContentLength is > ImportLimits.MaxMultipartBytes)
        {
            throw new StatementRejectedException(ParserReasons.FileTooLarge);
        }

        string? boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        var body = new BoundedReadStream(request.Body, ImportLimits.MaxMultipartBytes);
        var reader = new MultipartReader(boundary, body);
        byte[]? file = null;
        string? options = null;
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
        {
            var disposition = section.GetContentDispositionHeader();
            if (disposition is null || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
            {
                throw new StatementRejectedException(ParserReasons.InvalidRequest);
            }
            string? name = disposition.Name.Value?.Trim('"');
            switch (name)
            {
                case "file" when file is null:
                    file = await ReadBytesAsync(section.Body, ImportLimits.MaxFileBytes, cancellationToken);
                    break;
                case "options" when options is null:
                    options = await ReadTextAsync(section.Body, ImportLimits.MaxMultipartOptionsBytes, cancellationToken);
                    break;
                default:
                    throw new StatementRejectedException(ParserReasons.InvalidRequest);
            }
        }

        if (file is null || options is null)
        {
            throw new StatementRejectedException(ParserReasons.InvalidRequest);
        }

        return new MultipartImport(file, options);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maximum)
            {
                throw new StatementRejectedException(ParserReasons.FileTooLarge);
            }
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static async Task<string> ReadTextAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBytesAsync(stream, maximum, cancellationToken);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new StatementRejectedException(ParserReasons.UnsupportedEncoding);
        }
    }

    private sealed class BoundedReadStream(Stream inner, int maximum) : Stream
    {
        private int _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            Count(read);

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            Count(read);

            return read;
        }

        private void Count(int read)
        {
            _read += read;
            if (_read > maximum)
            {
                throw new StatementRejectedException(ParserReasons.FileTooLarge);
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
