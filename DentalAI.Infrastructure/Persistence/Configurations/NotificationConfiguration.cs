using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class ClinicNotificationSettingsConfiguration : IEntityTypeConfiguration<ClinicNotificationSettings>
{
    public void Configure(EntityTypeBuilder<ClinicNotificationSettings> builder)
    {
        builder.ToTable("ClinicNotificationSettings");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.ClinicId).IsUnique().HasFilter("[ClinicId] IS NOT NULL");
    }
}

public sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("OutboundMessages");
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.To).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(300);
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(100);
        builder.Property(x => x.NotBeforeUtc).HasColumnType("datetime2");
        builder.Property(x => x.ClaimedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.SentAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");

        builder.HasIndex(x => new { x.Status, x.NotBeforeUtc });           // the sender's queue
        builder.HasIndex(x => new { x.ClinicId, x.CreatedDate });           // the clinic's message log
        builder.HasIndex(x => new { x.AppointmentId, x.Kind, x.ReminderHours });
        builder.HasIndex(x => new { x.To, x.SentAtUtc });                   // "C" replies
    }
}
