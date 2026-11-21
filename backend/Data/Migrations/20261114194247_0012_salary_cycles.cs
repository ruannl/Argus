using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class _0012_salary_cycles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "salary_cycle_starts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                start_date = table.Column<DateOnly>(type: "date", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("salary_cycle_starts_pkey", x => x.id);
                table.UniqueConstraint("salary_cycle_starts_customer_date_key", x => new { x.customer_id, x.start_date });
                table.UniqueConstraint("salary_cycle_starts_customer_id_id_key", x => new { x.customer_id, x.id });
                table.ForeignKey(
                    name: "salary_cycle_starts_customer_id_fkey",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "salary_cycle_state",
            columns: table => new
            {
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("salary_cycle_state_pkey", x => x.customer_id);
                table.CheckConstraint("salary_cycle_state_revision_check", "revision > 0");
                table.ForeignKey(
                    name: "salary_cycle_state_customer_id_fkey",
                    column: x => x.customer_id,
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Prefix 12 adds its own forced-RLS policies and least-privilege grants.
        // Migration 0011 keeps its frozen 13-table contract and is never
        // rewritten to reference these tables.
        migrationBuilder.Sql(CustomerIsolationSql.BuildSalaryCycleIsolationSql());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("salary-cycle migrations do not support destructive reversal");
}
