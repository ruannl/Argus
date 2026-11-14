
using Microsoft.EntityFrameworkCore.Migrations;

namespace argus.Server.Data.Migrations;

public partial class M0011CustomerRowSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CustomerIsolationSql.BuildUpSql());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("customer row-security migrations do not support destructive reversal");
}
