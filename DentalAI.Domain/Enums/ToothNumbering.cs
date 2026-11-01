namespace DentalAI.Domain.Enums;

/// <summary>How teeth are numbered on invoices and charts.</summary>
public enum ToothNumbering
{
    /// <summary>US: 1–32 for permanent teeth, A–T for primary teeth.</summary>
    Universal = 0,
    /// <summary>International (ISO 3950): two digits, 11–48 permanent, 51–85 primary.</summary>
    Fdi = 1
}
