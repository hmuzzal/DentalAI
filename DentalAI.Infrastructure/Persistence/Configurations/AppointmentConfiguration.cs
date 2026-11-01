using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.StartTime).HasColumnType("datetime2");
        builder.Property(x => x.EndTime).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");

        builder.HasIndex(x => x.ClinicId);
        builder.HasIndex(x => x.DoctorId);
        builder.HasIndex(x => x.PatientId);
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.StartTime);
        builder.HasIndex(x => new { x.ClinicId, x.DoctorId, x.StartTime });
        builder.HasIndex(x => new { x.ClinicId, x.Status, x.StartTime });

        builder.HasOne(x => x.Clinic)
            .WithMany(x => x.Appointments)
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Doctor)
            .WithMany(x => x.Appointments)
            .HasForeignKey(x => x.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Patient)
            .WithMany(x => x.Appointments)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Service)
            .WithMany(x => x.Appointments)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
