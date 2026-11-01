using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class DoctorBreakConfiguration : IEntityTypeConfiguration<DoctorBreak>
{
    public void Configure(EntityTypeBuilder<DoctorBreak> builder)
    {
        builder.ToTable("DoctorBreaks");
        builder.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.StartTime).HasColumnType("time");
        builder.Property(x => x.EndTime).HasColumnType("time");
        builder.HasIndex(x => new { x.ClinicId, x.DoctorId, x.DayOfWeek });
        builder.HasOne(x => x.Doctor).WithMany(x => x.Breaks).HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Cascade);
    }
}
