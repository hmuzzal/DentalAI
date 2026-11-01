using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

public sealed class DoctorLeaveConfiguration : IEntityTypeConfiguration<DoctorLeave>
{
    public void Configure(EntityTypeBuilder<DoctorLeave> builder)
    {
        builder.ToTable("DoctorLeaves");
        builder.Property(x => x.StartDate).HasColumnType("date");
        builder.Property(x => x.EndDate).HasColumnType("date");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.ClinicId, x.DoctorId, x.StartDate, x.EndDate });
        builder.HasOne(x => x.Doctor).WithMany(x => x.Leaves).HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Cascade);
    }
}
