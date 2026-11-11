using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using argus.Server.Data;

#nullable disable

namespace argus.Server.Data.Migrations
{
    [DbContext(typeof(ArgusDbContext))]
    [Migration("20261110000003_0003_password_reset_tokens")]
    partial class M0003PasswordResetTokens
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            ArgusPhase2MigrationModel.Build(modelBuilder);
        }
    }
}
