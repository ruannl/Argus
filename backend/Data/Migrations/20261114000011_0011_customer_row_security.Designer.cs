using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using argus.Server.Data;

#nullable disable

namespace argus.Server.Data.Migrations
{
    [DbContext(typeof(ArgusDbContext))]
    [Migration("20261114000011_0011_customer_row_security")]
    partial class M0011CustomerRowSecurity
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            ArgusPhase3MigrationModel.Build(modelBuilder);
        }
    }
}
