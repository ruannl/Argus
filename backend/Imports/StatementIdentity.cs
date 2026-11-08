using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace argus.Server.Imports;

/// <summary>
/// Canonical row identity and statement fingerprint. Both are versioned and
/// length-delimited so no field boundary can be forged by embedded separators.
/// </summary>
public static class StatementIdentity
{
    public static string ComputeIdentityKey(DateOnly transactionDate, long cents, string normalizedDescription)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt(hash, ImportLimits.IdentityVersion);
        AppendString(hash, transactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendString(hash, (cents == 0 ? 0 : cents).ToString(CultureInfo.InvariantCulture));
        AppendString(hash, normalizedDescription);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string ComputeStatementFingerprint(IReadOnlyList<ParsedStatementRow> rows, DateOnly startDate, DateOnly endDate)
    {
        var signatures = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            signatures.Add(RowSignature(row));
        }
        signatures.Sort(StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt(hash, ImportLimits.DuplicatePolicyVersion);
        AppendString(hash, startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendString(hash, endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendInt(hash, signatures.Count);
        foreach (string signature in signatures)
        {
            AppendString(hash, signature);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string RowSignature(ParsedStatementRow row)
    {
        var builder = new StringBuilder();
        AppendField(builder, "date_raw", row.RawValues.GetValueOrDefault("date"));
        AppendField(builder, "date", row.TransactionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendField(builder, "description", row.Description);
        AppendField(builder, "normalized", row.NormalizedDescription);
        AppendField(builder, "amount_raw", row.RawValues.GetValueOrDefault("amount"));
        AppendField(builder, "amount_cents", row.AmountCents?.ToString(CultureInfo.InvariantCulture));
        AppendField(builder, "id_namespace", row.SourceIdNamespace);
        AppendField(builder, "id_value", row.SourceIdValue);
        AppendField(builder, "validation", string.Join(',', row.ValidationCodes.Order(StringComparer.Ordinal)));

        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string name, string? value)
    {
        string text = value ?? "<null>";
        builder.Append(name).Append('=').Append(text.Length).Append(':').Append(text).Append('|');
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendInt(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
