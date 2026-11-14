using Microsoft.EntityFrameworkCore;

#nullable disable

namespace argus.Server.Data.Migrations;

/// <summary>
/// Target model for the customer-isolation migration. The frozen Phase 2
/// baseline keeps migrations 0001-0010 stable; this model adds only the
/// customer-inclusive alias relationships introduced by migration 0011.
/// </summary>
public static class ArgusPhase3MigrationModel
{
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgusPhase2MigrationModel.Build(modelBuilder);

        modelBuilder.Entity("argus.Server.Data.Entities.MerchantAlias", builder =>
        {
            builder.HasOne("argus.Server.Data.Entities.MerchantTag", null)
                .WithMany()
                .HasForeignKey("CustomerId", "MerchantTagId")
                .HasPrincipalKey("CustomerId", "Id")
                .HasConstraintName("merchant_aliases_customer_tag_fk")
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne("argus.Server.Data.Entities.Category", null)
                .WithMany()
                .HasForeignKey("CustomerId", "CategoryId")
                .HasPrincipalKey("CustomerId", "Id")
                .HasConstraintName("merchant_aliases_customer_category_fk")
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
