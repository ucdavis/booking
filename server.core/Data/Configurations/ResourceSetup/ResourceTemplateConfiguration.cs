using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Data.Configurations.ResourceSetup;

public class ResourceTemplateConfiguration : IEntityTypeConfiguration<ResourceTemplate>
{
    public void Configure(EntityTypeBuilder<ResourceTemplate> builder)
    {
        builder.ToTable("ResourceTemplates", table =>
        {
            table.HasCheckConstraint("CK_ResourceTemplates_Form", "FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
            table.HasCheckConstraint("CK_ResourceTemplates_Defaults", "ResourceDefaultsJson IS NULL OR ISJSON(ResourceDefaultsJson) = 1");
        });

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).UseIdentityColumn();
        builder.Property(template => template.Name).HasMaxLength(200).IsRequired();
        builder.Property(template => template.FormJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(template => template.ResourceDefaultsJson).HasColumnType("nvarchar(max)");
        builder.Property(template => template.IsActive).HasDefaultValue(true);

        builder.HasOne(template => template.Team)
            .WithMany()
            .HasForeignKey(template => template.TeamId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(template => template.UpdatedByUser)
            .WithMany()
            .HasForeignKey(template => template.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
