
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0003PasswordResetTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0003_password_reset_tokens",
            "to_regclass('password_reset_tokens') IS NOT NULL",
            LegacySchemaSql.M0003PasswordResetTokens));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
