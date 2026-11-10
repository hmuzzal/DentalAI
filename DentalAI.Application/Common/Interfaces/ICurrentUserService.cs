using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Interfaces;

public interface ICurrentUserService
{
    /// <summary>The signed-in staff user. Null for patients on the portal.</summary>
    Guid? UserId { get; }
    Guid? ClinicId { get; }
    /// <summary>The signed-in portal patient. Null for staff.</summary>
    Guid? PatientId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    bool IsInRole(UserRole role);
    bool CanAccessClinic(Guid? clinicId);
}
