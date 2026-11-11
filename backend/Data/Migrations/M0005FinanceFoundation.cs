
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0005FinanceFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0005_finance_foundation",
            "to_regclass('financial_accounts') IS NOT NULL AND to_regclass('categories') IS NOT NULL AND to_regclass('merchant_tags') IS NOT NULL AND to_regclass('statement_imports') IS NOT NULL AND to_regclass('transactions') IS NOT NULL AND to_regclass('transaction_merchant_tags') IS NOT NULL AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'customers' AND column_name = 'categories_seeded_at')",
            LegacySchemaSql.M0005FinanceFoundation));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
