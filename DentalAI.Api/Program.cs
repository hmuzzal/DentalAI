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
    .AddJwtBearer(AuthSchemes.Staff, options => ConfigureBearer(options, jwtOptions.Audience))
    .AddJwtBearer(AuthSchemes.Portal, options => ConfigureBearer(options, jwtOptions.PortalAudience));

void ConfigureBearer(JwtBearerOptions options, string audience)
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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PortalRateLimits.SignIn, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = portalSignInPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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

            // The AlignLegacy*Schema patches below were written against the schema as of this migration, and
            // later migrations (e.g. AddSchedulingEngine indexes Appointments.StartTime) assume they have run.
            // On a database that hasn't reached it yet (a fresh install), stop there, align, then continue.
            // Only ever migrate forward to it: MigrateAsync(target) would roll back a database that is past it.
            const string legacyAlignmentBaseline = "20260621095058_SecureMultiTenantAuthAndPatientsV2";
            if (pendingMigrations.Contains(legacyAlignmentBaseline))
            {
                await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
                    .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(dbContext)
                    .MigrateAsync(legacyAlignmentBaseline);
                await AlignLegacySchemaAsync(dbContext, logger);
            }

            await dbContext.Database.MigrateAsync();

            logger.LogInformation("Database migrations applied successfully.");
        }

        await AlignLegacySchemaAsync(dbContext, logger);
        var seeder = services.GetRequiredService<ApplicationDbContextSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
        logger.LogInformation("Seed process completed successfully.");
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

static async Task AlignLegacySchemaAsync(ApplicationDbContext dbContext, Microsoft.Extensions.Logging.ILogger logger)
{
    await AlignLegacyClinicSchemaAsync(dbContext, logger, CancellationToken.None);
    await AlignLegacyDoctorSchemaAsync(dbContext, logger, CancellationToken.None);
    await AlignLegacyUserSchemaAsync(dbContext, logger, CancellationToken.None);
    await AlignLegacyPatientSchemaAsync(dbContext, logger, CancellationToken.None);
    await AlignLegacyAppointmentSchemaAsync(dbContext, logger, CancellationToken.None);
}

static async Task AlignLegacyClinicSchemaAsync(
    ApplicationDbContext dbContext,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

    if (shouldCloseConnection)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        var clinicColumns = await GetTableColumnsAsync(connection, "Clinics", cancellationToken);
        if (clinicColumns.Count == 0 || clinicColumns.Contains("TimeZone"))
        {
            return;
        }

        logger.LogWarning("Aligning legacy Clinics table schema in-place.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Clinics', 'TimeZone') IS NULL
                ALTER TABLE [dbo].[Clinics] ADD [TimeZone] nvarchar(100) NOT NULL CONSTRAINT [DF_Clinics_TimeZone] DEFAULT N'UTC';
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Legacy Clinics table schema aligned successfully.");
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task AlignLegacyDoctorSchemaAsync(
    ApplicationDbContext dbContext,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

    if (shouldCloseConnection)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        var doctorColumns = await GetTableColumnsAsync(connection, "Doctors", cancellationToken);
        if (doctorColumns.Count == 0)
        {
            return;
        }

        var expectedColumns = new[] { "Email", "FirstName", "LastName", "Phone", "Specialty" };
        if (expectedColumns.All(doctorColumns.Contains))
        {
            return;
        }

        var hasLegacyColumns = doctorColumns.Contains("FullName") && doctorColumns.Contains("Specialization");
        if (!hasLegacyColumns)
        {
            logger.LogWarning(
                "Doctors table is missing expected columns ({MissingColumns}) but does not match the legacy schema shape.",
                expectedColumns.Where(column => !doctorColumns.Contains(column)).ToArray());
            return;
        }

        logger.LogWarning("Aligning legacy Doctors table schema in-place.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Doctors_ClinicId_FullName' AND object_id = OBJECT_ID(N'dbo.Doctors'))
                DROP INDEX [IX_Doctors_ClinicId_FullName] ON [dbo].[Doctors];

            IF COL_LENGTH('dbo.Doctors', 'Specialization') IS NOT NULL AND COL_LENGTH('dbo.Doctors', 'Specialty') IS NULL
                EXEC sp_rename 'dbo.Doctors.Specialization', 'Specialty', 'COLUMN';

            IF COL_LENGTH('dbo.Doctors', 'Email') IS NULL
                ALTER TABLE [dbo].[Doctors] ADD [Email] nvarchar(200) NOT NULL CONSTRAINT [DF_Doctors_Email] DEFAULT N'';

            IF COL_LENGTH('dbo.Doctors', 'FirstName') IS NULL
                ALTER TABLE [dbo].[Doctors] ADD [FirstName] nvarchar(100) NOT NULL CONSTRAINT [DF_Doctors_FirstName] DEFAULT N'';

            IF COL_LENGTH('dbo.Doctors', 'LastName') IS NULL
                ALTER TABLE [dbo].[Doctors] ADD [LastName] nvarchar(100) NOT NULL CONSTRAINT [DF_Doctors_LastName] DEFAULT N'';

            IF COL_LENGTH('dbo.Doctors', 'Phone') IS NULL
                ALTER TABLE [dbo].[Doctors] ADD [Phone] nvarchar(30) NOT NULL CONSTRAINT [DF_Doctors_Phone] DEFAULT N'';
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Doctors', 'FullName') IS NOT NULL
            BEGIN
                UPDATE [dbo].[Doctors]
                SET
                    [FirstName] = CASE
                        WHEN LTRIM(RTRIM([FullName])) = N'' THEN N''
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) - 1)
                        ELSE LTRIM(RTRIM([FullName]))
                    END,
                    [LastName] = CASE
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                        ELSE N''
                    END
                WHERE ISNULL([FirstName], N'') = N'' AND ISNULL([LastName], N'') = N'';
            END

            IF COL_LENGTH('dbo.Doctors', 'FullName') IS NOT NULL
                ALTER TABLE [dbo].[Doctors] DROP COLUMN [FullName];

            IF COL_LENGTH('dbo.Doctors', 'IsActive') IS NOT NULL
                ALTER TABLE [dbo].[Doctors] DROP COLUMN [IsActive];

            IF COL_LENGTH('dbo.Doctors', 'WorkDayEnd') IS NOT NULL
                ALTER TABLE [dbo].[Doctors] DROP COLUMN [WorkDayEnd];

            IF COL_LENGTH('dbo.Doctors', 'WorkDayStart') IS NOT NULL
                ALTER TABLE [dbo].[Doctors] DROP COLUMN [WorkDayStart];

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Doctors_ClinicId' AND object_id = OBJECT_ID(N'dbo.Doctors'))
                CREATE INDEX [IX_Doctors_ClinicId] ON [dbo].[Doctors] ([ClinicId]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Doctors_ClinicId_LastName_FirstName' AND object_id = OBJECT_ID(N'dbo.Doctors'))
                CREATE INDEX [IX_Doctors_ClinicId_LastName_FirstName] ON [dbo].[Doctors] ([ClinicId], [LastName], [FirstName]);
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Legacy Doctors table schema aligned successfully.");
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task AlignLegacyUserSchemaAsync(
    ApplicationDbContext dbContext,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

    if (shouldCloseConnection)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        var userColumns = await GetTableColumnsAsync(connection, "Users", cancellationToken);
        if (userColumns.Count == 0)
        {
            return;
        }

        var requiresAlignment = userColumns.Contains("FullName")
            || !userColumns.Contains("FirstName")
            || !userColumns.Contains("LastName")
            || !userColumns.Contains("RefreshToken")
            || !userColumns.Contains("RefreshTokenExpiry");

        if (!requiresAlignment)
        {
            await EnsureColumnNullableAsync(connection, "Users", "ClinicId", cancellationToken);
            return;
        }

        logger.LogWarning("Aligning legacy Users table schema in-place.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Users', 'FirstName') IS NULL
                ALTER TABLE [dbo].[Users] ADD [FirstName] nvarchar(100) NOT NULL CONSTRAINT [DF_Users_FirstName] DEFAULT N'';

            IF COL_LENGTH('dbo.Users', 'LastName') IS NULL
                ALTER TABLE [dbo].[Users] ADD [LastName] nvarchar(100) NOT NULL CONSTRAINT [DF_Users_LastName] DEFAULT N'';

            IF COL_LENGTH('dbo.Users', 'RefreshToken') IS NULL
                ALTER TABLE [dbo].[Users] ADD [RefreshToken] nvarchar(200) NULL;

            IF COL_LENGTH('dbo.Users', 'RefreshTokenExpiry') IS NULL
                ALTER TABLE [dbo].[Users] ADD [RefreshTokenExpiry] datetime2 NULL;
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Users', 'FullName') IS NOT NULL
            BEGIN
                UPDATE [dbo].[Users]
                SET
                    [FirstName] = CASE
                        WHEN LTRIM(RTRIM([FullName])) = N'' THEN N''
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) - 1)
                        ELSE LTRIM(RTRIM([FullName]))
                    END,
                    [LastName] = CASE
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                        ELSE N''
                    END
                WHERE ISNULL([FirstName], N'') = N'' AND ISNULL([LastName], N'') = N'';

                ALTER TABLE [dbo].[Users] DROP COLUMN [FullName];
            END
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        await EnsureColumnNullableAsync(connection, "Users", "ClinicId", cancellationToken);
        logger.LogInformation("Legacy Users table schema aligned successfully.");
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task AlignLegacyAppointmentSchemaAsync(
    ApplicationDbContext dbContext,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

    if (shouldCloseConnection)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        var appointmentColumns = await GetTableColumnsAsync(connection, "Appointments", cancellationToken);
        if (appointmentColumns.Count == 0)
        {
            return;
        }

        // ServiceId is a live column (AddSchedulingEngine migration); it must not be treated as legacy.
        var requiresAlignment = appointmentColumns.Contains("PatientName")
            || appointmentColumns.Contains("StartAt")
            || appointmentColumns.Contains("EndAt")
            || appointmentColumns.Contains("ScheduledStartUtc")
            || appointmentColumns.Contains("ScheduledEndUtc")
            || !appointmentColumns.Contains("PatientId")
            || !appointmentColumns.Contains("StartTime")
            || !appointmentColumns.Contains("EndTime");

        if (!requiresAlignment)
        {
            return;
        }

        logger.LogWarning("Aligning legacy Appointments table schema in-place.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Appointments', 'StartTime') IS NULL AND COL_LENGTH('dbo.Appointments', 'StartAt') IS NOT NULL
                EXEC sp_rename 'dbo.Appointments.StartAt', 'StartTime', 'COLUMN';

            IF COL_LENGTH('dbo.Appointments', 'EndTime') IS NULL AND COL_LENGTH('dbo.Appointments', 'EndAt') IS NOT NULL
                EXEC sp_rename 'dbo.Appointments.EndAt', 'EndTime', 'COLUMN';

            IF COL_LENGTH('dbo.Appointments', 'StartTime') IS NULL
                ALTER TABLE [dbo].[Appointments] ADD [StartTime] datetime2 NOT NULL CONSTRAINT [DF_Appointments_StartTime] DEFAULT '0001-01-01T00:00:00.0000000';

            IF COL_LENGTH('dbo.Appointments', 'EndTime') IS NULL
                ALTER TABLE [dbo].[Appointments] ADD [EndTime] datetime2 NOT NULL CONSTRAINT [DF_Appointments_EndTime] DEFAULT '0001-01-01T00:00:00.0000000';

            IF COL_LENGTH('dbo.Appointments', 'Notes') IS NULL
                ALTER TABLE [dbo].[Appointments] ADD [Notes] nvarchar(2000) NULL;

            IF COL_LENGTH('dbo.Appointments', 'PatientId') IS NULL
                ALTER TABLE [dbo].[Appointments] ADD [PatientId] uniqueidentifier NULL;
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Appointments', 'ScheduledStartUtc') IS NOT NULL
                EXEC(N'UPDATE [dbo].[Appointments] SET [StartTime] = [ScheduledStartUtc] WHERE [StartTime] = ''0001-01-01T00:00:00.0000000'';');

            IF COL_LENGTH('dbo.Appointments', 'ScheduledEndUtc') IS NOT NULL
                EXEC(N'UPDATE [dbo].[Appointments] SET [EndTime] = [ScheduledEndUtc] WHERE [EndTime] = ''0001-01-01T00:00:00.0000000'';');

            IF COL_LENGTH('dbo.Appointments', 'PatientName') IS NOT NULL
                EXEC(N'
                INSERT INTO [dbo].[Patients] ([Id], [ClinicId], [FirstName], [LastName], [Phone], [Email], [DateOfBirth], [Gender], [Notes], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy], [IsDeleted])
                SELECT NEWID(),
                       source.[ClinicId],
                       CASE
                           WHEN LTRIM(RTRIM(source.[PatientName])) = N'''' THEN N''Legacy''
                           WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LEFT(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) - 1)
                           ELSE LTRIM(RTRIM(source.[PatientName]))
                       END,
                       CASE
                           WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) + 1, LEN(LTRIM(RTRIM(source.[PatientName])))))
                           ELSE N''Patient''
                       END,
                       N'''',
                       NULL,
                       NULL,
                       NULL,
                       NULL,
                       SYSUTCDATETIME(),
                       N''migration'',
                       NULL,
                       NULL,
                       0
                FROM (
                    SELECT DISTINCT [ClinicId], [PatientName]
                    FROM [dbo].[Appointments]
                    WHERE ISNULL(LTRIM(RTRIM([PatientName])), N'''') <> N''''
                ) AS source
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [dbo].[Patients] AS p
                    WHERE p.[ClinicId] = source.[ClinicId]
                      AND p.[FirstName] = CASE
                          WHEN LTRIM(RTRIM(source.[PatientName])) = N'''' THEN N''Legacy''
                          WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LEFT(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) - 1)
                          ELSE LTRIM(RTRIM(source.[PatientName]))
                      END
                      AND p.[LastName] = CASE
                          WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) + 1, LEN(LTRIM(RTRIM(source.[PatientName])))))
                          ELSE N''Patient''
                      END
                      AND p.[IsDeleted] = 0
                );

                UPDATE appointment
                SET [PatientId] = patient.[Id]
                FROM [dbo].[Appointments] AS appointment
                INNER JOIN [dbo].[Patients] AS patient
                    ON patient.[ClinicId] = appointment.[ClinicId]
                   AND patient.[FirstName] = CASE
                       WHEN LTRIM(RTRIM(appointment.[PatientName])) = N'''' THEN N''Legacy''
                       WHEN CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) > 0 THEN LEFT(LTRIM(RTRIM(appointment.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) - 1)
                       ELSE LTRIM(RTRIM(appointment.[PatientName]))
                   END
                   AND patient.[LastName] = CASE
                       WHEN CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(appointment.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) + 1, LEN(LTRIM(RTRIM(appointment.[PatientName])))))
                       ELSE N''Patient''
                   END
                   AND patient.[IsDeleted] = 0
                WHERE appointment.[PatientId] IS NULL;');

            IF EXISTS (SELECT 1 FROM [dbo].[Appointments] WHERE [PatientId] IS NULL)
            BEGIN
                DECLARE @fallbackClinicId uniqueidentifier;
                DECLARE clinic_cursor CURSOR FAST_FORWARD FOR
                    SELECT DISTINCT [ClinicId] FROM [dbo].[Appointments] WHERE [PatientId] IS NULL;

                OPEN clinic_cursor;
                FETCH NEXT FROM clinic_cursor INTO @fallbackClinicId;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    DECLARE @fallbackPatientId uniqueidentifier = (
                        SELECT TOP 1 [Id]
                        FROM [dbo].[Patients]
                        WHERE [ClinicId] = @fallbackClinicId
                          AND [FirstName] = N'Legacy'
                          AND [LastName] = N'Patient'
                          AND [IsDeleted] = 0
                    );

                    IF @fallbackPatientId IS NULL
                    BEGIN
                        SET @fallbackPatientId = NEWID();
                        INSERT INTO [dbo].[Patients] ([Id], [ClinicId], [FirstName], [LastName], [Phone], [Email], [DateOfBirth], [Gender], [Notes], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy], [IsDeleted])
                        VALUES (@fallbackPatientId, @fallbackClinicId, N'Legacy', N'Patient', N'', NULL, NULL, NULL, N'Backfilled during schema alignment.', SYSUTCDATETIME(), N'migration', NULL, NULL, 0);
                    END

                    UPDATE [dbo].[Appointments]
                    SET [PatientId] = @fallbackPatientId
                    WHERE [ClinicId] = @fallbackClinicId
                      AND [PatientId] IS NULL;

                    FETCH NEXT FROM clinic_cursor INTO @fallbackClinicId;
                END

                CLOSE clinic_cursor;
                DEALLOCATE clinic_cursor;
            END

            -- Indexes on the legacy columns (e.g. InitialCreate's IX_Appointments_*_ScheduledStartUtc) block
            -- dropping them, so drop those indexes first.
            DECLARE @legacyIndexSql nvarchar(max) = N'';
            SELECT @legacyIndexSql = @legacyIndexSql + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON [dbo].[Appointments];'
            FROM sys.indexes AS i
            WHERE i.object_id = OBJECT_ID(N'dbo.Appointments')
              AND i.is_primary_key = 0
              AND EXISTS (
                  SELECT 1
                  FROM sys.index_columns AS ic
                  INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id
                    AND ic.index_id = i.index_id
                    AND c.name IN (N'PatientName', N'ScheduledStartUtc', N'ScheduledEndUtc'));
            IF @legacyIndexSql <> N''
                EXEC sp_executesql @legacyIndexSql;

            IF COL_LENGTH('dbo.Appointments', 'PatientName') IS NOT NULL
                EXEC(N'
                DECLARE @constraintName sysname;
                SELECT @constraintName = dc.name
                FROM sys.default_constraints AS dc
                INNER JOIN sys.columns AS c
                    ON c.object_id = dc.parent_object_id
                   AND c.column_id = dc.parent_column_id
                WHERE dc.parent_object_id = OBJECT_ID(N''dbo.Appointments'')
                  AND c.name = N''PatientName'';

                IF @constraintName IS NOT NULL
                    EXEC(N''ALTER TABLE [dbo].[Appointments] DROP CONSTRAINT ['' + @constraintName + N'']'');

                ALTER TABLE [dbo].[Appointments] DROP COLUMN [PatientName];');

            IF COL_LENGTH('dbo.Appointments', 'ScheduledStartUtc') IS NOT NULL
                EXEC(N'
                DECLARE @constraintName sysname;
                SELECT @constraintName = dc.name
                FROM sys.default_constraints AS dc
                INNER JOIN sys.columns AS c
                    ON c.object_id = dc.parent_object_id
                   AND c.column_id = dc.parent_column_id
                WHERE dc.parent_object_id = OBJECT_ID(N''dbo.Appointments'')
                  AND c.name = N''ScheduledStartUtc'';

                IF @constraintName IS NOT NULL
                    EXEC(N''ALTER TABLE [dbo].[Appointments] DROP CONSTRAINT ['' + @constraintName + N'']'');

                ALTER TABLE [dbo].[Appointments] DROP COLUMN [ScheduledStartUtc];');

            IF COL_LENGTH('dbo.Appointments', 'ScheduledEndUtc') IS NOT NULL
                EXEC(N'
                DECLARE @constraintName sysname;
                SELECT @constraintName = dc.name
                FROM sys.default_constraints AS dc
                INNER JOIN sys.columns AS c
                    ON c.object_id = dc.parent_object_id
                   AND c.column_id = dc.parent_column_id
                WHERE dc.parent_object_id = OBJECT_ID(N''dbo.Appointments'')
                  AND c.name = N''ScheduledEndUtc'';

                IF @constraintName IS NOT NULL
                    EXEC(N''ALTER TABLE [dbo].[Appointments] DROP CONSTRAINT ['' + @constraintName + N'']'');

                ALTER TABLE [dbo].[Appointments] DROP COLUMN [ScheduledEndUtc];');
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Appointments')
                  AND name = 'PatientId'
                  AND is_nullable = 1
            )
                ALTER TABLE [dbo].[Appointments] ALTER COLUMN [PatientId] uniqueidentifier NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_PatientId' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                CREATE INDEX [IX_Appointments_PatientId] ON [dbo].[Appointments] ([PatientId]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_DoctorId' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                CREATE INDEX [IX_Appointments_DoctorId] ON [dbo].[Appointments] ([DoctorId]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_ClinicId_DoctorId_StartTime' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                CREATE INDEX [IX_Appointments_ClinicId_DoctorId_StartTime] ON [dbo].[Appointments] ([ClinicId], [DoctorId], [StartTime]);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Appointments_Patients_PatientId')
                ALTER TABLE [dbo].[Appointments] ADD CONSTRAINT [FK_Appointments_Patients_PatientId]
                FOREIGN KEY ([PatientId]) REFERENCES [dbo].[Patients]([Id]) ON DELETE NO ACTION;
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Legacy Appointments table schema aligned successfully.");
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task AlignLegacyPatientSchemaAsync(
    ApplicationDbContext dbContext,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    var connection = dbContext.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

    if (shouldCloseConnection)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        var patientColumns = await GetTableColumnsAsync(connection, "Patients", cancellationToken);
        if (patientColumns.Count == 0)
        {
            return;
        }

        var requiresAlignment = !patientColumns.Contains("FirstName")
            || !patientColumns.Contains("LastName")
            || !patientColumns.Contains("Phone")
            || !patientColumns.Contains("Email")
            || !patientColumns.Contains("DateOfBirth")
            || !patientColumns.Contains("Gender")
            || !patientColumns.Contains("Notes");

        if (!requiresAlignment)
        {
            return;
        }

        logger.LogWarning("Aligning legacy Patients table schema in-place.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Patients', 'FirstName') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [FirstName] nvarchar(100) NOT NULL CONSTRAINT [DF_Patients_FirstName] DEFAULT N'';

            IF COL_LENGTH('dbo.Patients', 'LastName') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [LastName] nvarchar(100) NOT NULL CONSTRAINT [DF_Patients_LastName] DEFAULT N'';

            IF COL_LENGTH('dbo.Patients', 'Phone') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [Phone] nvarchar(30) NOT NULL CONSTRAINT [DF_Patients_Phone] DEFAULT N'';

            IF COL_LENGTH('dbo.Patients', 'Email') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [Email] nvarchar(200) NULL;

            IF COL_LENGTH('dbo.Patients', 'DateOfBirth') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [DateOfBirth] date NULL;

            IF COL_LENGTH('dbo.Patients', 'Gender') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [Gender] nvarchar(30) NULL;

            IF COL_LENGTH('dbo.Patients', 'Notes') IS NULL
                ALTER TABLE [dbo].[Patients] ADD [Notes] nvarchar(2000) NULL;
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            """
            IF COL_LENGTH('dbo.Patients', 'Name') IS NOT NULL
            BEGIN
                UPDATE [dbo].[Patients]
                SET
                    [FirstName] = CASE
                        WHEN LTRIM(RTRIM([Name])) = N'' THEN N'Legacy'
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([Name]))) > 0 THEN LEFT(LTRIM(RTRIM([Name])), CHARINDEX(N' ', LTRIM(RTRIM([Name]))) - 1)
                        ELSE LTRIM(RTRIM([Name]))
                    END,
                    [LastName] = CASE
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([Name]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([Name])), CHARINDEX(N' ', LTRIM(RTRIM([Name]))) + 1, LEN(LTRIM(RTRIM([Name])))))
                        ELSE N'Patient'
                    END
                WHERE ISNULL([FirstName], N'') = N'' AND ISNULL([LastName], N'') = N'';
            END

            IF COL_LENGTH('dbo.Patients', 'FullName') IS NOT NULL
            BEGIN
                UPDATE [dbo].[Patients]
                SET
                    [FirstName] = CASE
                        WHEN LTRIM(RTRIM([FullName])) = N'' THEN N'Legacy'
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) - 1)
                        ELSE LTRIM(RTRIM([FullName]))
                    END,
                    [LastName] = CASE
                        WHEN CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(N' ', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                        ELSE N'Patient'
                    END
                WHERE ISNULL([FirstName], N'') = N'' AND ISNULL([LastName], N'') = N'';
            END

            UPDATE [dbo].[Patients]
            SET [FirstName] = N'Legacy'
            WHERE ISNULL([FirstName], N'') = N'';

            UPDATE [dbo].[Patients]
            SET [LastName] = N'Patient'
            WHERE ISNULL([LastName], N'') = N'';
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Legacy Patients table schema aligned successfully.");
    }
    finally
    {
        if (shouldCloseConnection)
        {
            await connection.CloseAsync();
        }
    }
}

static async Task<HashSet<string>> GetTableColumnsAsync(
    System.Data.Common.DbConnection connection,
    string tableName,
    CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT COLUMN_NAME
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = @tableName
        """;

    var tableNameParameter = command.CreateParameter();
    tableNameParameter.ParameterName = "@tableName";
    tableNameParameter.Value = tableName;
    command.Parameters.Add(tableNameParameter);

    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        columns.Add(reader.GetString(0));
    }

    return columns;
}

static async Task ExecuteNonQueryAsync(
    System.Data.Common.DbConnection connection,
    System.Data.Common.DbTransaction transaction,
    string commandText,
    CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = commandText;
    await command.ExecuteNonQueryAsync(cancellationToken);
}

static async Task EnsureColumnNullableAsync(
    System.Data.Common.DbConnection connection,
    string tableName,
    string columnName,
    CancellationToken cancellationToken)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@tableName)
              AND name = @columnName
              AND is_nullable = 0
        ) THEN 1 ELSE 0 END
        """;

    var tableParameter = command.CreateParameter();
    tableParameter.ParameterName = "@tableName";
    tableParameter.Value = $"dbo.{tableName}";
    command.Parameters.Add(tableParameter);

    var columnParameter = command.CreateParameter();
    columnParameter.ParameterName = "@columnName";
    columnParameter.Value = columnName;
    command.Parameters.Add(columnParameter);

    var requiresUpdate = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    if (!requiresUpdate)
    {
        return;
    }

    await using var alterCommand = connection.CreateCommand();
    alterCommand.CommandText = $"ALTER TABLE [dbo].[{tableName}] ALTER COLUMN [{columnName}] uniqueidentifier NULL";
    await alterCommand.ExecuteNonQueryAsync(cancellationToken);
}

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
