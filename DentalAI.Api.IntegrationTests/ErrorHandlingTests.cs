using System.Text.Json;
using DentalAI.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>500 responses must not reveal exception details outside Development.</summary>
public sealed class ErrorHandlingTests
{
    private const string Secret = "Invalid column 'PasswordHash' in table 'Users' at Server=prod-sql";

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public async Task UnhandledErrors_ShowDetailsOnlyInDevelopment(string environment, bool showsDetails)
    {
        var (status, body) = await RunAsync(environment, _ => throw new InvalidOperationException(Secret));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(showsDetails, body.Contains("PasswordHash"));
        Assert.Contains("reference trace-123", body);
    }

    [Fact]
    public async Task LockTimeouts_Return409WithTheirOwnMessage()
    {
        var (status, body) = await RunAsync("Production", _ => throw new TimeoutException("This record is busy. Please try again."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("busy", body);
    }

    private static async Task<(int Status, string Body)> RunAsync(string environment, RequestDelegate next)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance, new TestEnvironment(environment));

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        JsonDocument.Parse(body); // always valid JSON
        return (context.Response.StatusCode, body);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DentalAI.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
