namespace DentalAI.Domain.Enums;

/// <summary>How a patient prefers to be contacted. <see cref="Sms"/> needs SMS consent on file.</summary>
public enum ContactMethod
{
    Phone = 0,
    Sms = 1,
    Email = 2
}
