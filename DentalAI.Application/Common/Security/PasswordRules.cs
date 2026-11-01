using FluentValidation;

namespace DentalAI.Application.Common.Security;

/// <summary>Password policy for staff accounts (they can see patient data).</summary>
public static class PasswordRules
{
    public const int MinLength = 10;
    public const int MaxLength = 128;
    public const string Description = "At least 10 characters, with at least one letter and one number.";

    public static IRuleBuilderOptions<T, string> StaffPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter a password.")
            .MinimumLength(MinLength).WithMessage(Description)
            .MaximumLength(MaxLength)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit)).WithMessage(Description);
}
