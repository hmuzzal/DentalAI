using DentalAI.Infrastructure.Persistence;
using DentalAI.Application.Common.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DentalAI.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API against a throwaway SQL Server database, created by the API's own startup
/// (migrations + seed) and deleted when the test run ends.
///
/// The booking lock uses SQL Server's sp_getapplock, so these tests need SQL Server, not an in-memory
/// provider. Set DENTALAI_TEST_SQLSERVER to the server to use (default ".", Windows authentication).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"DentalAI_IntegrationTests_{Guid.NewGuid():N}";

    /// <summary>A fake Twilio auth token and public webhook URL, so tests can sign webhook requests.</summary>
    public const string TwilioTestAuthToken = "test-twilio-auth-token";
    public const string TwilioWebhookUrl = "https://api.test.local/api/webhooks/twilio/sms";

    /// <summary>The portal assistant's model: tests script its answers.</summary>
    public ScriptedChatModel ChatModel { get; } = new();

    private static string Server => Environment.GetEnvironmentVariable("DENTALAI_TEST_SQLSERVER") ?? ".";

    private string ConnectionString(string database) =>
        $"Server={Server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString(_databaseName));
        // The tests run the reminder and sending jobs themselves, at chosen times.
        builder.UseSetting("Jwt:SecretKey", "DentalAI-integration-tests-signing-key-0123456789");
        builder.UseSetting("Notifications:BackgroundJobsEnabled", "false");
        // Every test client shares one address; the per-IP sign-in limit is tested separately.
        builder.UseSetting("Portal:SignInRequestsPerMinute", "100000");
        builder.UseSetting("Ai:MessagesPerMinute", "100000");
        builder.UseSetting("Notifications:Sms:Twilio:AuthToken", TwilioTestAuthToken);
        builder.UseSetting("Notifications:Sms:Twilio:InboundWebhookUrl", TwilioWebhookUrl);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatModel>();
            services.AddSingleton<IChatModel>(ChatModel);
        });
    }

    public Task InitializeAsync()
    {
        // Building the server runs the API's startup: migrations and seed.
        _ = Server;
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetDbConnection().Database;

        // Safety net: never let the tests run against a real database.
        if (database != _databaseName)
        {
            throw new InvalidOperationException(
                $"Integration tests must use their own database, but the API is connected to '{database}'.");
        }

        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();

        // Set DENTALAI_TEST_KEEP_DB=1 to keep the database for inspection after a failed run.
        if (Environment.GetEnvironmentVariable("DENTALAI_TEST_KEEP_DB") == "1")
        {
            return;
        }

        await using var connection = new SqlConnection(ConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "API with SQL Server";
}
