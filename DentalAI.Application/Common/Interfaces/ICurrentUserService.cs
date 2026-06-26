using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? ClinicId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    bool IsInRole(UserRole role);
    bool CanAccessClinic(Guid? clinicId);
}
