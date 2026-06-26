using DentalAI.Application.Common.Interfaces;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalAI.Infrastructure.Persistence;

public sealed class ApplicationDbContextSeeder(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IDateTimeProvider dateTimeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return;
        }

        var clinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var superAdminId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var clinicAdminId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var receptionistId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var doctorId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var clinic = new Clinic
        {
            Id = clinicId,
            Name = "Demo Dental Clinic",
            Email = "clinic@dentalai.local",
            Phone = "+1-555-0101",
            Address = "123 Demo Street",
            TimeZone = "Asia/Dhaka",
            CreatedBy = "seed",
            CreatedDate = dateTimeProvider.UtcNow
        };

        var users = new[]
        {
            new User
            {
                Id = superAdminId,
                ClinicId = null,
                FirstName = "Super",
                LastName = "Admin",
                Email = "superadmin@dentalai.local",
                PasswordHash = passwordHasher.Hash("SuperAdmin123!"),
                Role = UserRole.SuperAdmin,
                IsActive = true,
                CreatedBy = "seed",
                CreatedDate = dateTimeProvider.UtcNow
            },
            new User
            {
                Id = clinicAdminId,
                ClinicId = clinicId,
                FirstName = "Demo",
                LastName = "Admin",
                Email = "clinicadmin@dentalai.local",
                PasswordHash = passwordHasher.Hash("ClinicAdmin123!"),
                Role = UserRole.ClinicAdmin,
                IsActive = true,
                CreatedBy = "seed",
                CreatedDate = dateTimeProvider.UtcNow
            },
            new User
            {
                Id = receptionistId,
                ClinicId = clinicId,
                FirstName = "Demo",
                LastName = "Receptionist",
                Email = "receptionist@dentalai.local",
                PasswordHash = passwordHasher.Hash("Receptionist123!"),
                Role = UserRole.Receptionist,
                IsActive = true,
                CreatedBy = "seed",
                CreatedDate = dateTimeProvider.UtcNow
            }
        };

        var doctor = new Doctor
        {
            Id = doctorId,
            ClinicId = clinicId,
            FirstName = "Sarah",
            LastName = "Ahmed",
            Specialty = "General Dentistry",
            Phone = "+1-555-0111",
            Email = "sarah.ahmed@dentalai.local",
            CreatedBy = "seed",
            CreatedDate = dateTimeProvider.UtcNow
        };

        await dbContext.Clinics.AddAsync(clinic, cancellationToken);
        await dbContext.Users.AddRangeAsync(users, cancellationToken);
        await dbContext.Doctors.AddAsync(doctor, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
