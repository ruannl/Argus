using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using argus.Server.Data;

#nullable disable

namespace argus.Server.Data.Migrations
{
    [DbContext(typeof(ArgusDbContext))]
    [Migration("20261110000002_0002_email_verification_tokens")]
    partial class M0002EmailVerificationTokens
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            ArgusPhase2MigrationModel.Build(modelBuilder);
        }
    }
}
