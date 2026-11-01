using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalAI.Infrastructure.Persistence.Configurations;

// Money is decimal(18,4): room for 3-decimal currencies (KWD, BHD, ...) and exact sums; amounts are rounded to the
// currency's own decimals by CurrencyRules before they're stored.

public sealed class ClinicBillingSettingsConfiguration : IEntityTypeConfiguration<ClinicBillingSettings>
{
    public void Configure(EntityTypeBuilder<ClinicBillingSettings> builder)
    {
        builder.ToTable("ClinicBillingSettings");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.InvoicePrefix).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DefaultTaxRatePercent).HasPrecision(7, 4);
        builder.Property(x => x.ToothNumbering).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.ClinicId).IsUnique().HasFilter("[ClinicId] IS NOT NULL");
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.Property(x => x.Number).HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        foreach (var money in new[] { nameof(Invoice.Subtotal), nameof(Invoice.DiscountAmount), nameof(Invoice.TaxAmount),
                     nameof(Invoice.Total), nameof(Invoice.InsuranceEstimate), nameof(Invoice.PatientResponsibility),
                     nameof(Invoice.AmountPaid), nameof(Invoice.Balance) })
        {
            builder.Property<decimal>(money).HasPrecision(18, 4);
        }
        builder.Property(x => x.TaxRatePercent).HasPrecision(7, 4);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.VoidReason).HasMaxLength(500);
        builder.Property(x => x.VoidedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");

        // Numbers are unique within a clinic (drafts have none).
        builder.HasIndex(x => new { x.ClinicId, x.Number }).IsUnique().HasFilter("[Number] IS NOT NULL");
        builder.HasIndex(x => new { x.ClinicId, x.Status });
        builder.HasIndex(x => new { x.ClinicId, x.PatientId });
        builder.HasIndex(x => x.AppointmentId);

        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines");
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ProcedureCode).HasMaxLength(20);
        builder.Property(x => x.Tooth).HasMaxLength(20);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 4);
        builder.Property(x => x.LineTotal).HasPrecision(18, 4);
        builder.Property(x => x.InsuranceEstimate).HasPrecision(18, 4);
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.InvoiceId);
        builder.HasOne(x => x.Invoice).WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.Property(x => x.Amount).HasPrecision(18, 4);
        builder.Property(x => x.RefundedAmount).HasPrecision(18, 4);
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.InvoiceId);
        builder.HasIndex(x => new { x.ClinicId, x.ReceivedAtUtc });
        builder.HasOne(x => x.Invoice).WithMany(x => x.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");
        builder.Property(x => x.Amount).HasPrecision(18, 4);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RefundedAtUtc).HasColumnType("datetime2");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime2");
        builder.HasIndex(x => x.PaymentId);
        builder.HasIndex(x => new { x.ClinicId, x.RefundedAtUtc });
        builder.HasOne(x => x.Payment).WithMany(x => x.Refunds).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
