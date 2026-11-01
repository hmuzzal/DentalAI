namespace DentalAI.Domain.Enums;

/// <summary>The patient's relationship to the policy subscriber (the person who holds the plan).</summary>
public enum InsuranceRelationship
{
    Self = 0,
    Spouse = 1,
    Child = 2,
    Other = 3
}
