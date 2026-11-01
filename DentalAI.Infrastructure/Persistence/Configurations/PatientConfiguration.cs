using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Gender).HasMaxLength(30);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.AddressLine1).HasMaxLength(200);
        builder.Property(x => x.AddressLine2).HasMaxLength(200);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.State).HasMaxLength(50);
        builder.Property(x => x.PostalCode).HasMaxLength(20);
        builder.Property(x => x.PreferredContactMethod).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(DentalAI.Domain.Enums.ContactMethod.Phone);
        builder.Property(x => x.SmsOptInAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.SmsOptOutAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");

        builder.HasIndex(x => new { x.ClinicId, x.Phone });
        builder.HasIndex(x => new { x.ClinicId, x.LastName, x.FirstName });

        builder.HasOne(x => x.Clinic)
            .WithMany(x => x.Patients)
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
