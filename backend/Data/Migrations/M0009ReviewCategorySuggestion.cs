
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0009ReviewCategorySuggestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LegacyGuard.Build(
            "0009_review_category_suggestion",
            "EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'import_review_rows' AND column_name = 'suggested_category_id') AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'import_review_rows' AND column_name = 'selected_category_id') AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'import_review_rows' AND column_name = 'category_decided')",
            LegacySchemaSql.M0009ReviewCategorySuggestion));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("legacy adoption migrations do not support destructive reversal");
}
