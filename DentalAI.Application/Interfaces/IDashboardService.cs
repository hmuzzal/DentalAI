using DentalAI.Application.Common.Models;
using DentalAI.Application.DTOs.Dashboard;

namespace DentalAI.Application.Interfaces;

public interface IDashboardService
{
    Task<Result<DashboardStatsDto>> GetStatsAsync(CancellationToken cancellationToken);
}
