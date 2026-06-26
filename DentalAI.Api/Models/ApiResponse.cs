namespace DentalAI.Api.Models;

public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }
    public IReadOnlyCollection<string>? Errors { get; init; }
}
