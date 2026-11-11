
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0006StatementImport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0006_statement_import",
            "to_regclass('import_reviews') IS NOT NULL AND to_regclass('import_review_rows') IS NOT NULL AND to_regclass('import_commit_receipts') IS NOT NULL AND to_regclass('import_quota_locks') IS NOT NULL AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'statement_imports' AND column_name = 'version') AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'transactions' AND column_name = 'identity_version')",
            LegacySchemaSql.M0006StatementImport));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
