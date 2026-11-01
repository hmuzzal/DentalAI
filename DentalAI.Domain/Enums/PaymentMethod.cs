namespace DentalAI.Domain.Enums;

public enum PaymentMethod
{
    Cash = 0,
    /// <summary>Card taken on a terminal in the clinic.</summary>
    Card = 1,
    Check = 2,
    /// <summary>Paid by the patient's insurer.</summary>
    Insurance = 3,
    /// <summary>Paid online through a payment link (Phase 2c).</summary>
    Online = 4,
    Other = 5
}
