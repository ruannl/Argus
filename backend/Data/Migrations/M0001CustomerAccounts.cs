
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0001CustomerAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS data_migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """);
        migrationBuilder.Sql(LegacyGuard.Build(
            "0001_customer_accounts",
            "to_regclass('customers') IS NOT NULL AND to_regclass('sessions') IS NOT NULL",
            LegacySchemaSql.M0001CustomerAccounts));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
