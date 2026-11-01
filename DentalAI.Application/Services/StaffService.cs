using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Application.Common.Models;
using DentalAI.Application.Common.Security;
using DentalAI.Application.DTOs.Staff;
using DentalAI.Application.Validators;
using DentalAI.Domain.Entities;
using DentalAI.Domain.Enums;

namespace DentalAI.Application.Services;

/// <summary>Who may manage a clinic's staff. Clinic admins manage their own clinic; platform admins any clinic.</summary>
public enum StaffActor
{
    ClinicAdmin,
    PlatformAdmin
}

/// <summary>
/// A clinic's staff accounts: invite (single-use link), change role, deactivate and reactivate.
/// Rules: nobody changes their own role or deactivates themselves; a clinic admin can't remove the clinic's last
/// active admin; platform admins only invite clinic admins (onboarding) and can deactivate anyone (e.g. to suspend).
/// </summary>
public sealed class StaffService(
    IUserRepository userRepository,
    IClinicRepository clinicRepository,
    ICurrentUserService currentUserService,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<IReadOnlyList<StaffMemberDto>>> ListAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        if (await clinicRepository.GetByIdAsync(clinicId, cancellationToken) is null)
            return Result<IReadOnlyList<StaffMemberDto>>.Fail(ResultStatus.NotFound, "Clinic was not found.");

        var users = await userRepository.ListByClinicAsync(clinicId, cancellationToken);
        return Result<IReadOnlyList<StaffMemberDto>>.Ok(users.Select(ToDto).ToList());
    }

    public async Task<Result<StaffInviteDto>> InviteAsync(
        Guid clinicId, InviteStaffRequest request, StaffActor actor, CancellationToken cancellationToken)
    {
        if (actor == StaffActor.PlatformAdmin && request.Role != UserRole.ClinicAdmin)
            return Result<StaffInviteDto>.Fail(ResultStatus.Forbidden,
                "Platform admins invite a clinic's admins; the clinic's admins invite the rest of their staff.");

        if (await clinicRepository.GetByIdAsync(clinicId, cancellationToken) is null)
            return Result<StaffInviteDto>.Fail(ResultStatus.NotFound, "Clinic was not found.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (await userRepository.GetByEmailAsync(email, cancellationToken) is not null)
            return Result<StaffInviteDto>.Fail(ResultStatus.Conflict, "Someone with this email already has an account.");

        var (token, hash) = InviteTokens.Create();
        var user = new User
        {
            ClinicId = clinicId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            Role = request.Role,
            IsActive = true,
            PasswordHash = string.Empty,
            InviteTokenHash = hash,
            InviteExpiresAtUtc = dateTimeProvider.UtcNow.Add(InviteTokens.Lifetime)
        };

        await userRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<StaffInviteDto>.Ok(new StaffInviteDto(ToDto(user), token, user.InviteExpiresAtUtc.Value),
            $"{user.FirstName} has been invited. Send them the link to set their password.");
    }

    /// <summary>A new link for someone who hasn't set a password yet. The previous link stops working.</summary>
    public async Task<Result<StaffInviteDto>> RenewInviteAsync(Guid clinicId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetInClinicAsync(userId, clinicId, cancellationToken);
        if (user is null) return Result<StaffInviteDto>.Fail(ResultStatus.NotFound, "Staff member was not found.");
        if (HasPassword(user))
            return Result<StaffInviteDto>.Fail(ResultStatus.Conflict, $"{user.FirstName} has already set a password.");
        if (!user.IsActive)
            return Result<StaffInviteDto>.Fail(ResultStatus.Conflict, $"Reactivate {user.FirstName} before sending a new link.");

        var (token, hash) = InviteTokens.Create();
        user.InviteTokenHash = hash;
        user.InviteExpiresAtUtc = dateTimeProvider.UtcNow.Add(InviteTokens.Lifetime);
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<StaffInviteDto>.Ok(new StaffInviteDto(ToDto(user), token, user.InviteExpiresAtUtc.Value),
            "A new invite link was created. The previous link no longer works.");
    }

    public async Task<Result<StaffMemberDto>> ChangeRoleAsync(
        Guid clinicId, Guid userId, UserRole role, CancellationToken cancellationToken)
    {
        if (!StaffRoles.IsClinicRole(role))
            return Result<StaffMemberDto>.Fail(ResultStatus.ValidationError, "Role must be ClinicAdmin or Receptionist.");

        var user = await userRepository.GetInClinicAsync(userId, clinicId, cancellationToken);
        if (user is null) return Result<StaffMemberDto>.Fail(ResultStatus.NotFound, "Staff member was not found.");
        if (user.Id == currentUserService.UserId)
            return Result<StaffMemberDto>.Fail(ResultStatus.Conflict, "You can't change your own role. Ask another admin.");
        if (user.Role == role) return Result<StaffMemberDto>.Ok(ToDto(user));

        if (user.Role == UserRole.ClinicAdmin && await IsLastActiveAdminAsync(clinicId, user, cancellationToken))
            return Result<StaffMemberDto>.Fail(ResultStatus.Conflict,
                $"{user.FirstName} is the clinic's only active admin. Make someone else an admin first.");

        user.Role = role;
        // A new role should take effect at the next sign-in, not after the old refresh token runs out.
        RevokeSessions(user);
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<StaffMemberDto>.Ok(ToDto(user), $"{user.FirstName} is now {(role == UserRole.ClinicAdmin ? "an admin" : "a receptionist")}.");
    }

    public async Task<Result<StaffMemberDto>> SetActiveAsync(
        Guid clinicId, Guid userId, bool active, StaffActor actor, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetInClinicAsync(userId, clinicId, cancellationToken);
        if (user is null) return Result<StaffMemberDto>.Fail(ResultStatus.NotFound, "Staff member was not found.");
        if (user.IsActive == active) return Result<StaffMemberDto>.Ok(ToDto(user));

        if (!active)
        {
            if (user.Id == currentUserService.UserId)
                return Result<StaffMemberDto>.Fail(ResultStatus.Conflict, "You can't deactivate your own account.");
            if (actor == StaffActor.ClinicAdmin && user.Role == UserRole.ClinicAdmin
                && await IsLastActiveAdminAsync(clinicId, user, cancellationToken))
                return Result<StaffMemberDto>.Fail(ResultStatus.Conflict,
                    $"{user.FirstName} is the clinic's only active admin. Make someone else an admin first.");
            RevokeSessions(user);
        }

        user.IsActive = active;
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<StaffMemberDto>.Ok(ToDto(user),
            active ? $"{user.FirstName} can sign in again." : $"{user.FirstName} can no longer sign in.");
    }

    /// <summary>True when no other active admin who has set a password would remain.</summary>
    private async Task<bool> IsLastActiveAdminAsync(Guid clinicId, User user, CancellationToken cancellationToken)
    {
        var staff = await userRepository.ListByClinicAsync(clinicId, cancellationToken);
        return !staff.Any(x => x.Id != user.Id && x.Role == UserRole.ClinicAdmin && x.IsActive && HasPassword(x));
    }

    private static void RevokeSessions(User user)
    {
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
    }

    private static bool HasPassword(User user) => !string.IsNullOrEmpty(user.PasswordHash);

    private StaffMemberDto ToDto(User user) => new(
        user.Id, user.FirstName, user.LastName, $"{user.FirstName} {user.LastName}".Trim(), user.Email, user.Role,
        StatusOf(user), HasPassword(user) ? null : user.InviteExpiresAtUtc, user.CreatedDate);

    private StaffStatus StatusOf(User user) =>
        !user.IsActive ? StaffStatus.Deactivated
        : HasPassword(user) ? StaffStatus.Active
        : user.InviteExpiresAtUtc > dateTimeProvider.UtcNow ? StaffStatus.Invited
        : StaffStatus.InviteExpired;
}
