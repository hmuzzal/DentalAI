using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RefreshToken).HasMaxLength(200);
        builder.Property(x => x.RefreshTokenExpiry).HasColumnType("datetime2");
        builder.Property(x => x.InviteTokenHash).HasMaxLength(64);
        builder.Property(x => x.InviteExpiresAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");

        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => new { x.ClinicId, x.Role });
        builder.HasIndex(x => x.InviteTokenHash).IsUnique().HasFilter("[InviteTokenHash] IS NOT NULL");

        builder.HasOne(x => x.Clinic)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
