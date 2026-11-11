
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0007TransactionWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0007_transaction_workspace",
            "to_regclass('transaction_operations') IS NOT NULL AND to_regclass('transaction_operation_items') IS NOT NULL",
            LegacySchemaSql.M0007TransactionWorkspace));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
