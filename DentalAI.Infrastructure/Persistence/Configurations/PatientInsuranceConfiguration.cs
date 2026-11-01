using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class PatientInsuranceConfiguration : IEntityTypeConfiguration<PatientInsurance>
{
    public void Configure(EntityTypeBuilder<PatientInsurance> builder)
    {
        builder.ToTable("PatientInsurances");
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RelationshipToPatient).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CarrierName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.MemberId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.GroupNumber).HasMaxLength(50);
        builder.Property(x => x.SubscriberName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");

        // One live policy per priority. Filtered so soft-deleted rows don't block a replacement.
        builder.HasIndex(x => new { x.PatientId, x.Priority }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => x.ClinicId);

        builder.HasOne(x => x.Patient)
            .WithMany(x => x.Insurances)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
