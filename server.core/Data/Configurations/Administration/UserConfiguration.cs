using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Administration;

namespace Server.Core.Data.Configurations.Administration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedOnAdd();
        builder.Property(user => user.IamId).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(user => user.Name).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(320);
        builder.Property(user => user.IsAdmin).HasDefaultValue(false);
        builder.Property(user => user.IsActive).HasDefaultValue(true);

        builder.HasIndex(user => user.IamId).IsUnique();
    }
}
