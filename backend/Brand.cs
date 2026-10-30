namespace argus.Server;

/// <summary>
/// Canonical product brand. Every structural brand string (header names, cookie
/// names, environment prefix) derives from these constants so a product rename
/// is mechanical. The frontend mirrors these values in src/lib/brand.ts.
/// </summary>
public static class Brand
{
    public const string ProductName = "Argus";
    public const string ProductToken = "81e1a205-ec1c-4e03-b785-9748db1e7677";
    public const string ProductSlug = "argus";
    public const string EnvPrefix = "ARGUS_";

    public const string ApplicationSecretHeader = "X-" + ProductToken + "-Application-Secret";
    public const string SessionHeader = "X-" + ProductToken + "-Session";
    public const string FrontendOriginHeader = "X-" + ProductToken + "-Frontend-Origin";

    public const string ApplicationSecretHeaderLowercase = "x-" + ProductToken + "-application-secret";
    public const string SessionHeaderLowercase = "x-" + ProductToken + "-session";
    public const string FrontendOriginHeaderLowercase = "x-" + ProductToken + "-frontend-origin";
}
