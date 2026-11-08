using argus.Server.Security;

namespace argus.Server.Tests;

public sealed class ConfigurationAndPolicyTests
{
    [Fact]
    public void MissingTestDatabaseUrl_FailsClosed()
    {
        var saved = Environment.GetEnvironmentVariable("ARGUS_TEST_DATABASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("ARGUS_TEST_DATABASE_URL", null);
            Assert.Throws<InvalidOperationException>(() => TestEnvironment.RequireTestDatabaseUrl());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARGUS_TEST_DATABASE_URL", saved);
        }
    }

    [Fact]
    public void PasswordPolicy_EnforcesLengthCommonListAndUnicodeCount()
    {
        Policy policy = new Policy();
        Assert.True(policy.CommonPasswordCount >= 1000);

        Assert.False(policy.ValidatePassword(null));
        Assert.False(policy.ValidatePassword("short"));
        Assert.False(policy.ValidatePassword(new string('a', 1025)));
        Assert.False(policy.ValidatePassword("Password123"));
        Assert.False(policy.ValidatePassword("123456"));
        Assert.True(policy.ValidatePassword("this is a compliant passphrase"));
    }

    [Fact]
    public void EmailCanonicalization_TrimsLowercasesAndRequiresLocalAndDomain()
    {
        Policy policy = new Policy();
        Assert.Equal("alice@example.test", policy.CanonicalizeEmail(" Alice@Example.TEST "));
        Assert.Null(policy.CanonicalizeEmail(null));
        Assert.Null(policy.CanonicalizeEmail(string.Empty));
        Assert.Null(policy.CanonicalizeEmail("missing-at"));
        Assert.Null(policy.CanonicalizeEmail("@example.test"));
        Assert.Null(policy.CanonicalizeEmail("local@"));
    }

    [Fact]
    public void Tokens_Are64LowercaseHex_AndHashDeterministically()
    {
        var token = Tokens.NewToken();
        Assert.True(Tokens.HasValidShape(token));
        Assert.Equal(token, token.ToLowerInvariant());
        Assert.Equal(Tokens.HashToken(token), Tokens.HashToken(token));
        Assert.NotEqual(token, Tokens.HashToken(token));

        Assert.False(Tokens.HasValidShape(null));
        Assert.False(Tokens.HasValidShape("abc"));
        Assert.False(Tokens.HasValidShape(new string('a', 63)));
        Assert.False(Tokens.HasValidShape(new string('a', 64).ToUpperInvariant()));
        Assert.False(Tokens.HasValidShape(new string('a', 63) + "g"));
    }

    [Fact]
    public void PasswordHasher_RoundTrips_AndRejectsTampering()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        Assert.StartsWith("argon2id$v=19$m=65536,t=3,p=1$", hash);
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(PasswordHasher.Verify("wrong password entirely", hash));
        Assert.False(PasswordHasher.Verify("correct horse battery staple", "not-a-valid-hash"));
    }
}
