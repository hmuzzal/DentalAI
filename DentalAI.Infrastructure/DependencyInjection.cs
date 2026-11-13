using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Interfaces.Persistence;
using DentalAI.Infrastructure.Ai;
using DentalAI.Infrastructure.Configuration;
using DentalAI.Application.Common.Notifications;
using DentalAI.Infrastructure.Notifications;
using DentalAI.Infrastructure.Persistence;
using DentalAI.Infrastructure.Persistence.Repositories;
using DentalAI.Infrastructure.Security;
using DentalAI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DentalAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddDbContext<ApplicationDbContext>(options =>
            options
                .UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IClinicRepository, ClinicRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDoctorRepository, DoctorRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<ISchedulingRepository, SchedulingRepository>();
        services.AddScoped<IPlatformRepository, PlatformRepository>();
        services.AddScoped<IBillingRepository, BillingRepository>();

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IPortalRepository, PortalRepository>();
        AddNotificationProviders(services, configuration);
        services.AddAiAssistant(configuration);

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddMemoryCache();
        services.AddSingleton<StaffAccessValidator>();
        services.AddSingleton<IStaffAccessCache>(sp => sp.GetRequiredService<StaffAccessValidator>());
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<ApplicationDbContextSeeder>();

        return services;
    }

    /// <summary>Email: "Log" (default) or "Smtp". SMS: "Log" (default) or "Twilio". See NotificationOptions.</summary>
    private static void AddNotificationProviders(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NotificationOptions.SectionName);
        services.Configure<NotificationOptions>(section);
        var options = section.Get<NotificationOptions>() ?? new NotificationOptions();
        services.AddSingleton<IAppLinks, AppLinks>();

        if (string.Equals(options.Email.Provider, "Smtp", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LogEmailSender>();

        if (string.Equals(options.Sms.Provider, "Twilio", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<ISmsSender, TwilioSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(20));
        else
            services.AddSingleton<ISmsSender, LogSmsSender>();
    }
}
