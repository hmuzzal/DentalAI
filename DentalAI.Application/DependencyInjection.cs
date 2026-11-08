using DentalAI.Application.Interfaces;
using DentalAI.Application.Mappings;
using DentalAI.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DentalAI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(ApplicationMappingProfile).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IClinicService, ClinicService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IServiceManagementService, ServiceManagementService>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<StaffService>();
        services.AddScoped<BillingService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ReminderScheduler>();
        services.AddScoped<MessageDispatcher>();
        services.AddScoped<InboundSmsHandler>();
        services.AddSingleton<SchedulingEngine>();

        return services;
    }
}
