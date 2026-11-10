using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class ClinicPortalSettingsConfiguration : IEntityTypeConfiguration<ClinicPortalSettings>
{
    public void Configure(EntityTypeBuilder<ClinicPortalSettings> builder)
    {
        builder.ToTable("ClinicPortalSettings");
        builder.Property(x => x.Slug).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.ClinicId).IsUnique().HasFilter("[ClinicId] IS NOT NULL");
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PortalSignInCodeConfiguration : IEntityTypeConfiguration<PortalSignInCode>
{
    public void Configure(EntityTypeBuilder<PortalSignInCode> builder)
    {
        builder.ToTable("PortalSignInCodes");
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Contact).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TicketHash).HasMaxLength(64);
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.VerifiedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.TicketExpiresAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.UsedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => new { x.ClinicId, x.Contact, x.CreatedDate });
        builder.HasIndex(x => x.TicketHash).HasFilter("[TicketHash] IS NOT NULL");
        builder.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PatientPortalSessionConfiguration : IEntityTypeConfiguration<PatientPortalSession>
{
    public void Configure(EntityTypeBuilder<PatientPortalSession> builder)
    {
        builder.ToTable("PatientPortalSessions");
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.RevokedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.LastUsedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.PatientId);
        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}
