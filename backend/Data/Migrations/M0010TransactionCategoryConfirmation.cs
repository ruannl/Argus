
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0010TransactionCategoryConfirmation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0010_transaction_category_confirmation",
            "EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'transactions' AND column_name = 'category_confirmed') AND to_regclass('transactions_customer_unconfirmed_idx') IS NOT NULL",
            LegacySchemaSql.M0010TransactionCategoryConfirmation));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
