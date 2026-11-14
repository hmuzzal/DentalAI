using System.Text;
using System.Text.Json.Serialization;
using DentalAI.Api.Infrastructure;
using DentalAI.Api.Middleware;
using DentalAI.Api.Models;
using DentalAI.Application;
using DentalAI.Application.Common.Interfaces;
using DentalAI.Application.Common.Portal;
using System.Threading.RateLimiting;
using DentalAI.Domain.Enums;
using DentalAI.Infrastructure;
using DentalAI.Infrastructure.Configuration;
using DentalAI.Infrastructure.Persistence;
using DentalAI.Application.Common.Ai;
using DentalAI.Infrastructure.Security;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "SpaCors";

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/dentalai-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Configuration.GetValue("Notifications:BackgroundJobsEnabled", true))
{
    // Reminders and sending. Off in integration tests, which run the jobs directly.
    builder.Services.AddHostedService<DentalAI.Api.Infrastructure.NotificationWorker>();
}
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DentalAI API",
        Version = "v1",
        Description = "Multi-tenant DentalAI SaaS Web API."
    });

    var jwtSecurityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter a JWT bearer token. Example: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };

    options.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [jwtSecurityScheme] = Array.Empty<string>()
    });
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Validation");
        var errors = context.ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? "Validation failed." : x.ErrorMessage)
            .ToArray();

        logger.LogWarning("Validation failed for {Path}: {Errors}", context.HttpContext.Request.Path, errors);

        return new BadRequestObjectResult(new ApiResponse
        {
            Success = false,
            Message = "Validation failed",
            Errors = errors
        });
    };
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
ValidateSigningKey(jwtOptions.SecretKey, builder.Environment);
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));
if (string.Equals(jwtOptions.Audience, jwtOptions.PortalAudience, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Jwt:PortalAudience must differ from Jwt:Audience, or patient tokens would work on staff endpoints.");
}

// Two bearer schemes with different audiences. The default picks one by path: /api/portal/* accepts patient tokens
// only, everything else staff tokens only. So a patient token is useless outside the portal, and vice versa.
builder.Services
    .AddAuthentication(AuthSchemes.Selector)
    .AddPolicyScheme(AuthSchemes.Selector, "Staff or portal", options =>
    {
        options.ForwardDefaultSelector = context =>
            AuthSchemes.IsPortalPath(context.Request.Path) ? AuthSchemes.Portal : AuthSchemes.Staff;
    })
    .AddJwtBearer(AuthSchemes.Staff, options => ConfigureBearer(options, jwtOptions.Audience, isStaff: true))
    .AddJwtBearer(AuthSchemes.Portal, options => ConfigureBearer(options, jwtOptions.PortalAudience, isStaff: false));

void ConfigureBearer(JwtBearerOptions options, string audience, bool isStaff)
{
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            // Staff tokens: the account must still be active, in a live clinic, with the role in the token, so
            // deactivating someone or changing their role takes effect now, not when the token expires.
            // (Patient tokens get the same treatment in PortalService: a disabled portal or deleted patient is a 401.)
            OnTokenValidated = async context =>
            {
                if (!isStaff || context.Principal is null) return;
                var validator = context.HttpContext.RequestServices.GetRequiredService<StaffAccessValidator>();
                var reason = await validator.CheckAsync(context.Principal, context.HttpContext.RequestAborted);
                if (reason is not null) context.Fail(reason);
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
                logger.LogWarning(context.Exception, "Authentication failed for {Path}", context.Request.Path);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
                logger.LogWarning("Authorization challenge triggered for {Path}", context.Request.Path);
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
                logger.LogWarning("Unauthorized access attempt by {User} for {Path}", context.HttpContext.User.Identity?.Name ?? "anonymous", context.Request.Path);
                return Task.CompletedTask;
            }
        };
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.SuperAdminOnly, policy =>
        policy.RequireRole(UserRole.SuperAdmin.ToString()));

    options.AddPolicy(AuthorizationPolicies.ClinicAdminOnly, policy =>
        policy.RequireRole(UserRole.ClinicAdmin.ToString()));

    // Clinic data: clinic staff only. Platform admins are deliberately excluded.
    options.AddPolicy(AuthorizationPolicies.ClinicStaff, policy =>
        policy.RequireRole(UserRole.ClinicAdmin.ToString(), UserRole.Receptionist.ToString()));

    // The patient portal: a patient token (its own scheme and audience) for one patient of one clinic.
    options.AddPolicy(AuthorizationPolicies.PortalPatient, policy => policy
        .AddAuthenticationSchemes(AuthSchemes.Portal)
        .RequireRole(PortalClaims.PatientRole)
        .RequireClaim(PortalClaims.PatientId)
        .RequireClaim(PortalClaims.ClinicId));
});

// Sign-in codes are guessable in principle: limit sign-in calls per client IP (on top of the per-contact limits).
var portalSignInPerMinute = Math.Max(1, builder.Configuration.GetValue("Portal:SignInRequestsPerMinute", 20));
var assistantMessagesPerMinute = Math.Max(1, builder.Configuration.GetValue("Ai:MessagesPerMinute", 12));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PortalRateLimits.SignIn, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = portalSignInPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(PortalRateLimits.Assistant, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = assistantMessagesPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellationToken) =>
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiResponse
        {
            Success = false,
            Message = "Too many attempts. Please wait a minute and try again."
        }, cancellationToken);
});

var allowedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200", "https://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy
            .WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DentalAI API v1");
    options.RoutePrefix = "swagger";
    options.DisplayRequestDuration();
});
app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    var chatModel = services.GetRequiredService<IChatModel>();
    logger.LogInformation("Portal assistant: {Provider}.", chatModel.IsEnabled ? chatModel.Provider : "off");
    if (chatModel.Provider == "Gemini" && !app.Environment.IsDevelopment())
    {
        logger.LogWarning("The portal assistant uses Gemini. Its free tier may use prompts to improve Google's models: "
            + "use it only with test data, or a paid plan whose terms cover health data.");
    }

    try
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();

        if (pendingMigrations.Length == 0)
        {
            logger.LogInformation("Database is up to date. No pending migrations were found.");
        }
        else
        {
            logger.LogInformation(
                "Applying {MigrationCount} pending migration(s): {Migrations}",
                pendingMigrations.Length,
                pendingMigrations);

            await dbContext.Database.MigrateAsync();

            logger.LogInformation("Database migrations applied successfully.");
        }

        var seeder = services.GetRequiredService<ApplicationDbContextSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
        logger.LogInformation("Seed process completed successfully.");
        await PatientPhoneBackfill.RunAsync(dbContext, logger, CancellationToken.None);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Database initialization failed.");
        throw;
    }
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;

    if (addresses is null || addresses.Count == 0)
    {
        logger.LogInformation("DentalAI API started successfully.");
        return;
    }

    foreach (var address in addresses)
    {
        logger.LogInformation("DentalAI API is running at {Address}", address);
        logger.LogInformation("Swagger UI is available at {SwaggerAddress}", $"{address.TrimEnd('/')}/swagger");
    }
});

app.Run();

// The token signing key comes from the environment (Jwt__SecretKey) or a secret store, never from appsettings.json.
// Anyone with the key can mint staff and patient tokens, so refuse to start with a weak or published one.
static void ValidateSigningKey(string key, IHostEnvironment environment)
{
    if (string.IsNullOrWhiteSpace(key))
    {
        throw new InvalidOperationException(
            "Jwt:SecretKey is not set. Set the Jwt__SecretKey environment variable (or a secret store) to a random value of at least 32 bytes, e.g. from `openssl rand -base64 48`.");
    }

    if (Encoding.UTF8.GetByteCount(key) < 32)
    {
        throw new InvalidOperationException("Jwt:SecretKey must be at least 32 bytes (256 bits) long.");
    }

    // Keys that are, or were, committed to the repository.
    string[] publishedKeys =
    [
        "DentalAI_Super_Long_Development_Key_Change_For_Production_2026",
        "DentalAI-local-development-signing-key-not-for-production"
    ];
    if (!environment.IsDevelopment() && publishedKeys.Contains(key, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"Jwt:SecretKey is a published development key and cannot be used in the {environment.EnvironmentName} environment. Set Jwt__SecretKey to a new random value.");
    }
}

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
