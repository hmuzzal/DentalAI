using DentalAI.Application.Common.Security;
using DentalAI.Application.DTOs.Auth;
using DentalAI.Application.DTOs.Staff;
using DentalAI.Domain.Enums;
using FluentValidation;

namespace DentalAI.Application.Validators;

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    public InviteStaffRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Role).Must(StaffRoles.IsClinicRole).WithMessage("Role must be ClinicAdmin or Receptionist.");
    }
}

public sealed class ChangeStaffRoleRequestValidator : AbstractValidator<ChangeStaffRoleRequest>
{
    public ChangeStaffRoleRequestValidator()
    {
        RuleFor(x => x.Role).Must(StaffRoles.IsClinicRole).WithMessage("Role must be ClinicAdmin or Receptionist.");
    }
}

public sealed class AcceptInviteRequestValidator : AbstractValidator<AcceptInviteRequest>
{
    public AcceptInviteRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).StaffPassword();
    }
}

public static class StaffRoles
{
    /// <summary>Roles a clinic's staff can have. Platform admins are never created through invites.</summary>
    public static bool IsClinicRole(UserRole role) => role is UserRole.ClinicAdmin or UserRole.Receptionist;
}
