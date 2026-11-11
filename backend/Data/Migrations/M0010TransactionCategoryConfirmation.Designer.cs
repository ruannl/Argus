using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using argus.Server.Data;

#nullable disable

namespace argus.Server.Data.Migrations
{
    [DbContext(typeof(ArgusDbContext))]
    [Migration("20261110000010_0010_transaction_category_confirmation")]
    partial class M0010TransactionCategoryConfirmation
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            ArgusPhase2MigrationModel.Build(modelBuilder);
        }
    }
}
