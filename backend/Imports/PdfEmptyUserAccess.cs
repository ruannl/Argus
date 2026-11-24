using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace argus.Server.Imports;

/// <summary>
/// Verifies the approved Standard Bank protection profile and that the document
/// authenticates with an empty USER password. This is a validation guard, not a
/// decryptor: PdfPig still supplies the decrypted pages after the guard passes.
/// </summary>
internal static class PdfEmptyUserAccess
{
    private const int ApprovedVersion = 5;
    private const int ApprovedRevision = 6;
    private const int ApprovedKeyLength = 256;
    private const int UserLength = 48;
    private const int OwnerLength = 48;
    private const int UserEncryptionLength = 32;
    private const int OwnerEncryptionLength = 32;
    private const int PermissionsLength = 16;
    private const int CryptFilterLength = 32;
    private const int HashLength = 32;
    private const int ValidationSaltLength = 8;
    private const int MaximumPasswordLength = 127;
    private const int MaximumVectorLength = 48;
    private const int KeyLength = 16;
    private const int IvLength = 16;
    private const int RepeatCount = 64;
    private const int RoundsBeforeTailCheck = 64;
    private const int RoundTailLength = 32;

    public static bool Verify(PdfDocument document)
    {
        if (!document.IsEncrypted)
        {
            return true;
        }

        try
        {
            if (!TryGetEncryptionDictionary(document, out DictionaryToken? dictionary) || dictionary is null)
            {
                return false;
            }

            if (!IsApprovedProtection(dictionary))
            {
                return false;
            }

            if (!TryGetBytes(dictionary, "U", out byte[]? user) || user is null || user.Length != UserLength)
            {
                return false;
            }

            byte[] validationSalt = user.AsSpan(HashLength, ValidationSaltLength).ToArray();
            byte[] expected = user.AsSpan(0, HashLength).ToArray();
            byte[] actual = ComputeR6Hash(ReadOnlySpan<byte>.Empty, validationSalt, ReadOnlySpan<byte>.Empty);

            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return false;
            }

            return ValidateUserKeyAndPermissions(dictionary, user);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns a same-length copy with the owner authentication fields replaced
    /// by zeros. The document is otherwise unchanged, so xref offsets and the
    /// verified user encryption fields remain valid. The caller uses this copy
    /// only to force the parsing library down the empty-user path.
    /// </summary>
    internal static byte[]? StripOwnerAuthentication(byte[] bytes)
    {
        try
        {
            string text = Encoding.Latin1.GetString(bytes);
            MatchCollection matches = Regex.Matches(
                text,
                @"/(?<name>O|OE)\s*<(?<hex>[0-9A-Fa-f\s]+)>",
                RegexOptions.CultureInvariant);
            bool hasOwner = false;
            bool hasOwnerEncryption = false;
            foreach (Match match in matches)
            {
                string name = match.Groups["name"].Value;
                if (name == "O")
                {
                    if (hasOwner)
                    {
                        return null;
                    }
                    hasOwner = true;
                }
                else
                {
                    if (hasOwnerEncryption)
                    {
                        return null;
                    }
                    hasOwnerEncryption = true;
                }
            }

            if (!hasOwner || !hasOwnerEncryption || matches.Count != 2)
            {
                return null;
            }

            StringBuilder builder = new(text);
            foreach (Match match in matches)
            {
                Group hex = match.Groups["hex"];
                for (int index = 0; index < hex.Length; index++)
                {
                    if (!char.IsWhiteSpace(hex.Value[index]))
                    {
                        builder[hex.Index + index] = '0';
                    }
                }
            }

            return Encoding.Latin1.GetBytes(builder.ToString());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool ValidateUserKeyAndPermissions(DictionaryToken dictionary, byte[] user)
    {
        if (!TryGetBytes(dictionary, "UE", out byte[]? userEncryption) || userEncryption?.Length != UserEncryptionLength)
        {
            return false;
        }

        if (!TryGetBytes(dictionary, "Perms", out byte[]? permissions) || permissions?.Length != PermissionsLength)
        {
            return false;
        }

        long? permissionValue = Number(dictionary, "P");
        if (permissionValue is null)
        {
            return false;
        }

        byte[] userKeySalt = user.AsSpan(HashLength + ValidationSaltLength, ValidationSaltLength).ToArray();
        byte[] intermediate = ComputeR6Hash(ReadOnlySpan<byte>.Empty, userKeySalt, ReadOnlySpan<byte>.Empty);
        byte[] fileKey = AesCbcDecrypt(userEncryption, intermediate, new byte[IvLength]);
        if (fileKey.Length != 32)
        {
            return false;
        }

        byte[] plainPermissions = AesEcbDecrypt(permissions, fileKey);
        if (plainPermissions.Length != PermissionsLength)
        {
            return false;
        }

        uint permissionsValue = unchecked((uint)permissionValue.Value);
        if (plainPermissions[0] != (byte)permissionsValue ||
            plainPermissions[1] != (byte)(permissionsValue >> 8) ||
            plainPermissions[2] != (byte)(permissionsValue >> 16) ||
            plainPermissions[3] != (byte)(permissionsValue >> 24))
        {
            return false;
        }

        for (int index = 4; index < 8; index++)
        {
            if (plainPermissions[index] != 0xFF)
            {
                return false;
            }
        }

        bool encryptMetadata = true;
        if (dictionary.Data.TryGetValue("EncryptMetadata", out IToken? metadata) && metadata is IDataToken<bool> metadataValue)
        {
            encryptMetadata = metadataValue.Data;
        }

        byte expectedMetadataFlag = encryptMetadata ? (byte)'T' : (byte)'F';
        return plainPermissions[8] == expectedMetadataFlag &&
               plainPermissions[9] == (byte)'a' &&
               plainPermissions[10] == (byte)'d' &&
               plainPermissions[11] == (byte)'b';
    }

    private static byte[] AesCbcDecrypt(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key.ToArray();
            aes.IV = iv.ToArray();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data.ToArray(), 0, data.Length);
        }
    }

    private static byte[] AesEcbDecrypt(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key.ToArray();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data.ToArray(), 0, data.Length);
        }
    }

    /// <summary>
    /// ISO 32000-2 Algorithm 2.B with the revision 6 hash selection. Exposed to
    /// the test assembly for independent known-answer vectors; the production
    /// caller only uses the empty-password user validation path.
    /// </summary>
    internal static byte[] ComputeR6Hash(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> vector)
    {
        if (salt.Length != ValidationSaltLength)
        {
            throw new ArgumentException("The revision 6 validation salt must be eight bytes.", nameof(salt));
        }

        if (vector.Length is > 0 and < MaximumVectorLength)
        {
            throw new ArgumentException("The revision 6 vector must be empty or at least 48 bytes.", nameof(vector));
        }

        if (vector.Length > MaximumVectorLength)
        {
            vector = vector[..MaximumVectorLength];
        }

        if (password.Length > MaximumPasswordLength)
        {
            password = password[..MaximumPasswordLength];
        }

        byte[] input = ComputeSha256(password, salt, vector);
        byte[] key = input[..KeyLength];
        byte[] iv = input[KeyLength..(KeyLength + IvLength)];
        byte[] round = [];
        int rounds = 0;
        while (rounds < RoundsBeforeTailCheck || rounds < round[^1] + RoundTailLength)
        {
            int unitLength = password.Length + input.Length + vector.Length;
            byte[] repeated = new byte[RepeatCount * unitLength];
            for (int index = 0; index < RepeatCount; index++)
            {
                int offset = index * unitLength;
                password.CopyTo(repeated.AsSpan(offset));
                offset += password.Length;
                input.CopyTo(repeated.AsSpan(offset));
                offset += input.Length;
                vector.CopyTo(repeated.AsSpan(offset));
            }

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                using ICryptoTransform encryptor = aes.CreateEncryptor();
                round = encryptor.TransformFinalBlock(repeated, 0, repeated.Length);
            }

            long sum = 0;
            for (int index = 0; index < 16; index++)
            {
                sum += round[index];
            }
            input = (sum % 3) switch
            {
                0 => SHA256.HashData(round),
                1 => SHA384.HashData(round),
                _ => SHA512.HashData(round),
            };
            key = input[..KeyLength];
            iv = input[KeyLength..(KeyLength + IvLength)];
            rounds++;
        }

        return input.Length > HashLength ? input[..HashLength] : input;
    }

    private static bool IsApprovedProtection(DictionaryToken dictionary)
    {
        if (!NameEquals(dictionary, "Filter", "Standard") ||
            Number(dictionary, "V") != ApprovedVersion ||
            Number(dictionary, "R") != ApprovedRevision ||
            Number(dictionary, "Length") != ApprovedKeyLength ||
            Number(dictionary, "P") is null ||
            !NameEquals(dictionary, "StmF", "StdCF") ||
            !NameEquals(dictionary, "StrF", "StdCF") ||
            !TryGetBytes(dictionary, "U", out byte[]? user) || user?.Length != UserLength ||
            !TryGetBytes(dictionary, "O", out byte[]? owner) || owner?.Length != OwnerLength ||
            !TryGetBytes(dictionary, "UE", out byte[]? userEncryption) || userEncryption?.Length != UserEncryptionLength ||
            !TryGetBytes(dictionary, "OE", out byte[]? ownerEncryption) || ownerEncryption?.Length != OwnerEncryptionLength ||
            !TryGetBytes(dictionary, "Perms", out byte[]? permissions) || permissions?.Length != PermissionsLength)
        {
            return false;
        }

        if (!dictionary.Data.TryGetValue("CF", out IToken? cryptFiltersToken) ||
            cryptFiltersToken is not DictionaryToken cryptFilters ||
            !cryptFilters.Data.TryGetValue("StdCF", out IToken? standardToken) ||
            standardToken is not DictionaryToken standard)
        {
            return false;
        }

        return NameEquals(standard, "CFM", "AESV3") && Number(standard, "Length") == CryptFilterLength;
    }

    private static bool TryGetEncryptionDictionary(PdfDocument document, out DictionaryToken? dictionary)
    {
        dictionary = null;
        IToken? token = document.Structure.Trailer.EncryptionToken;
        switch (token)
        {
            case IndirectReferenceToken reference:
                ObjectToken? objectToken = document.Structure.GetObject(reference.Data);
                dictionary = objectToken?.Data as DictionaryToken;
                break;
            case DictionaryToken direct:
                dictionary = direct;
                break;
        }

        return dictionary is not null;
    }

    private static bool NameEquals(DictionaryToken dictionary, string key, string expected) =>
        dictionary.Data.TryGetValue(key, out IToken? value) &&
        value is NameToken name &&
        string.Equals(name.Data, expected, StringComparison.Ordinal);

    private static long? Number(DictionaryToken dictionary, string key) =>
        dictionary.Data.TryGetValue(key, out IToken? value) && value is NumericToken number ? number.Long : null;

    private static bool TryGetBytes(DictionaryToken dictionary, string key, out byte[]? bytes)
    {
        bytes = null;
        if (!dictionary.Data.TryGetValue(key, out IToken? value))
        {
            return false;
        }

        bytes = value switch
        {
            HexToken hex => hex.Bytes.ToArray(),
            StringToken text => text.GetBytes(),
            _ => null,
        };

        return bytes is not null;
    }

    private static byte[] ComputeSha256(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> vector)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(password);
        hash.AppendData(salt);
        hash.AppendData(vector);

        return hash.GetHashAndReset();
    }
}
