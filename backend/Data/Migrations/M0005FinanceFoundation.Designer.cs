using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using argus.Server.Data;

#nullable disable

namespace argus.Server.Data.Migrations
{
    [DbContext(typeof(ArgusDbContext))]
    [Migration("20261110000005_0005_finance_foundation")]
    partial class M0005FinanceFoundation
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            ArgusPhase2MigrationModel.Build(modelBuilder);
        }
    }
}
