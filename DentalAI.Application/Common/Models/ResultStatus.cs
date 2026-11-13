namespace DentalAI.Application.Common.Models;

public enum ResultStatus
{
    Success = 1,
    ValidationError = 2,
    NotFound = 3,
    Unauthorized = 4,
    Forbidden = 5,
    Conflict = 6,
    Error = 7,
    TooManyRequests = 8,
    /// <summary>A service the request depends on (e.g. the AI model) is down or turned off.</summary>
    Unavailable = 9
}
