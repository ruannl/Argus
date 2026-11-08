using System.Text;

namespace argus.Server.Imports;

public sealed record CsvRecord(int StartLine,
                               int EndLine,
                               IReadOnlyList<string> Fields,
                               bool Malformed);

/// <summary>
/// Strict RFC-4180-style reader used by both CSV presets. It tracks physical
/// line numbers (including quoted multiline records) and reports malformed
/// quoting rather than repairing it.
/// </summary>
public static class CsvRecordReader
{
    public static IReadOnlyList<CsvRecord> Read(string text,
                                                char delimiter,
                                                int maxRecords = int.MaxValue,
                                                CancellationToken cancellationToken = default)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        int line = 1;
        int recordStart = 1;
        bool inQuotes = false;
        bool recordHasContent = false;
        bool malformed = false;

        for (int index = 0; index < text.Length; index++)
        {
            if ((index & 0x1FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            char character = text[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                        if (index + 1 < text.Length && text[index + 1] != delimiter && text[index + 1] != '\r' && text[index + 1] != '\n')
                        {
                            malformed = true;
                        }
                    }
                }
                else
                {
                    if (character == '\n')
                    {
                        line++;
                    }
                    field.Append(character);
                }
                continue;
            }

            if (character == '"')
            {
                if (field.Length == 0)
                {
                    inQuotes = true;
                    recordHasContent = true;
                }
                else
                {
                    malformed = true;
                    field.Append(character);
                }
                continue;
            }

            if (character == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                recordHasContent = true;
                continue;
            }

            if (character == '\r' || character == '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
                fields.Add(field.ToString());
                field.Clear();
                if (recordHasContent || fields.Count > 1)
                {
                    if (records.Count >= maxRecords)
                    {
                        throw new StatementRejectedException(ParserReasons.ProcessingLimit);
                    }
                    records.Add(new CsvRecord(recordStart, line, fields.ToArray(), malformed));
                }
                fields.Clear();
                line++;
                recordStart = line;
                recordHasContent = false;
                malformed = false;
                continue;
            }

            field.Append(character);
            recordHasContent = true;
        }

        if (inQuotes)
        {
            malformed = true;
        }

        if (recordHasContent || field.Length > 0 || fields.Count > 0)
        {
            if (records.Count >= maxRecords)
            {
                throw new StatementRejectedException(ParserReasons.ProcessingLimit);
            }
            fields.Add(field.ToString());
            records.Add(new CsvRecord(recordStart, line, fields.ToArray(), malformed));
        }

        return records;
    }
}
