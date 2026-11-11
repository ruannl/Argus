
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0002EmailVerificationTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0002_email_verification_tokens",
            "to_regclass('email_verification_tokens') IS NOT NULL",
            LegacySchemaSql.M0002EmailVerificationTokens));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
