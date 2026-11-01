using System.Linq.Expressions;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Domain.Common;
using DentalAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DentalAI.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentUserService currentUserService,
    IDateTimeProvider dateTimeProvider) : DbContext(options), IUnitOfWork
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DentalAI.Domain.Entities.Service> Services => Set<DentalAI.Domain.Entities.Service>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<DoctorBreak> DoctorBreaks => Set<DoctorBreak>();
    public DbSet<DoctorLeave> DoctorLeaves => Set<DoctorLeave>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    private Guid? CurrentClinicId => currentUserService.ClinicId;
    private bool BypassTenantFilter => currentUserService.IsSuperAdmin;

    /// <summary>
    /// Every DateTime column stores UTC. SQL Server's datetime2 has no kind, so mark values read back as UTC;
    /// otherwise they serialize without a "Z" and clients parse them as local time.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    internal sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            ApplyGlobalFilters(modelBuilder, entityType);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var diagnostics = exception.Entries.Select(BuildConcurrencyEntryDiagnostics).ToArray();
            throw new DbUpdateConcurrencyException(
                $"A concurrency conflict occurred while saving changes. Entries: {string.Join(" | ", diagnostics)}",
                exception);
        }
    }

    public async Task<T> RunWithDoctorScheduleLockAsync<T>(
        Guid doctorId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("The doctor schedule lock must not be nested inside another transaction.");
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        // Transaction-owned app lock: released automatically on commit, rollback or connection loss.
        // Every booking write for this doctor takes the same lock, so availability checks see committed data.
        var lockResult = new Microsoft.Data.SqlClient.SqlParameter("@result", System.Data.SqlDbType.Int)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        var resource = new Microsoft.Data.SqlClient.SqlParameter("@resource", System.Data.SqlDbType.NVarChar, 255)
        {
            Value = $"dentalai:doctor-schedule:{doctorId:N}"
        };
        await Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;",
            [resource, lockResult],
            cancellationToken);

        if (lockResult.Value is not int code || code < 0)
        {
            throw new TimeoutException("The doctor's schedule is busy. Please try again.");
        }

        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private void ApplyAuditInformation()
    {
        var utcNow = dateTimeProvider.UtcNow;
        var actor = currentUserService.Email ?? "system";

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedDate = utcNow;
                    entry.Entity.CreatedBy = actor;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedDate = utcNow;
                    entry.Entity.UpdatedBy = actor;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedDate = utcNow;
                    entry.Entity.UpdatedBy = actor;
                    break;
            }
        }
    }

    private void ApplyGlobalFilters(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        var method = GetType()
            .GetMethod(nameof(SetGlobalFilter), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(entityType.ClrType);

        method.Invoke(this, [modelBuilder]);
    }

    private void SetGlobalFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : BaseEntity
    {
        if (typeof(ITenantEntity).IsAssignableFrom(typeof(TEntity)))
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(BuildTenantFilter<TEntity>());
            return;
        }

        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => !entity.IsDeleted);
    }

    private Expression<Func<TEntity, bool>> BuildTenantFilter<TEntity>() where TEntity : BaseEntity
    {
        return entity => !entity.IsDeleted
            && (BypassTenantFilter
                || (CurrentClinicId.HasValue
                    && EF.Property<Guid?>(entity, nameof(ITenantEntity.ClinicId)).HasValue
                    && EF.Property<Guid?>(entity, nameof(ITenantEntity.ClinicId)) == CurrentClinicId.Value));
    }

    private static string BuildConcurrencyEntryDiagnostics(EntityEntry entry)
    {
        var keyValues = entry.Properties
            .Where(property => property.Metadata.IsPrimaryKey())
            .Select(property => $"{property.Metadata.Name}={property.CurrentValue ?? "null"}");

        return $"{entry.Metadata.ClrType.Name}:{entry.State}[{string.Join(", ", keyValues)}]";
    }
}
