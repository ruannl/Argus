
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0008SuggestionLearning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0008_suggestion_learning",
            "to_regclass('merchant_aliases') IS NOT NULL AND to_regprocedure('argus_merchant_key(text)') IS NOT NULL AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'transactions' AND column_name = 'merchant_key' AND is_generated = 'ALWAYS')",
            LegacySchemaSql.M0008SuggestionLearning));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
