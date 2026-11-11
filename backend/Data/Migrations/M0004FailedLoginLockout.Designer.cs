using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using argus.Server.Data;

#nullable disable

namespace argus.Server.Data.Migrations
{
    [DbContext(typeof(ArgusDbContext))]
    [Migration("20261110000004_0004_failed_login_lockout")]
    partial class M0004FailedLoginLockout
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            ArgusPhase2MigrationModel.Build(modelBuilder);
        }
    }
}
