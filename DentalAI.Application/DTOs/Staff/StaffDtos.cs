using DentalAI.Domain.Enums;

namespace DentalAI.Application.DTOs.Staff;

public enum StaffStatus
{
    /// <summary>Has set a password and can sign in.</summary>
    Active,
    /// <summary>Invited; the link hasn't been used yet and hasn't expired.</summary>
    Invited,
    /// <summary>Invited, but the link expired. Send a new one.</summary>
    InviteExpired,
    /// <summary>Can't sign in.</summary>
    Deactivated
}

public sealed record StaffMemberDto(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    UserRole Role,
    StaffStatus Status,
    DateTime? InviteExpiresAtUtc,
    DateTime CreatedDate);

/// <summary>
/// Returned when an invite is created or renewed. <c>InviteToken</c> is shown only here, once; the app turns it into
/// a link (/auth/accept-invite?token=...) for the admin to send. It can't be fetched again, only replaced.
/// </summary>
public sealed record StaffInviteDto(StaffMemberDto Member, string InviteToken, DateTime InviteExpiresAtUtc);

public sealed class InviteStaffRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>ClinicAdmin or Receptionist.</summary>
    public UserRole Role { get; set; } = UserRole.Receptionist;
}

public sealed class ChangeStaffRoleRequest
{
    public UserRole Role { get; set; }
}
