using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class DoctorScheduleConfiguration : IEntityTypeConfiguration<DoctorSchedule>
{
    public void Configure(EntityTypeBuilder<DoctorSchedule> builder)
    {
        builder.ToTable("DoctorSchedules");
        builder.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.StartTime).HasColumnType("time");
        builder.Property(x => x.EndTime).HasColumnType("time");
        builder.HasIndex(x => new { x.ClinicId, x.DoctorId, x.DayOfWeek });
        builder.HasOne(x => x.Doctor).WithMany(x => x.Schedules).HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Cascade);
    }
}
