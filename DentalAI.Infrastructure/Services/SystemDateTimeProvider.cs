using DentalAI.Application.Common.Interfaces;

namespace DentalAI.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
