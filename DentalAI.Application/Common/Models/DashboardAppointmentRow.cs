using DentalAI.Domain.Enums;

namespace DentalAI.Application.Common.Models;

/// <summary>One appointment as the dashboard needs it: when (UTC), its status, its price and its clinic's zone.</summary>
public sealed record DashboardAppointmentRow(DateTime StartUtc, AppointmentStatus Status, decimal Price, string? TimeZoneId);
