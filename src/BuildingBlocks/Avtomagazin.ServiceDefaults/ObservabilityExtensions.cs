using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Avtomagazin.ServiceDefaults;

public static class ObservabilityExtensions
{
    public const string RequestIdHeader = "X-Request-Id";

    public static IHostApplicationBuilder AddAvtomagazinObservability(this IHostApplicationBuilder builder)
    {
        var useJson = DeploySecrets.IsPublic(builder.Environment)
                      || builder.Configuration.GetValue("Logging:Json", false);

        builder.Logging.ClearProviders();
        if (useJson)
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
                options.UseUtcTimestamp = true;
                options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.ColorBehavior = LoggerColorBehavior.Enabled;
            });
        }

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"]);

        return builder;
    }

    public static IHostApplicationBuilder AddAvtomagazinPostgresHealth(
        this IHostApplicationBuilder builder,
        string name,
        string connectionString)
    {
        if (builder.Environment.IsEnvironment("Testing") || string.IsNullOrWhiteSpace(connectionString))
        {
            return builder;
        }

        builder.Services.AddHealthChecks()
            .AddCheck(name, new NpgsqlHealthCheck(connectionString), tags: ["ready"]);
        return builder;
    }

    public static IHostApplicationBuilder AddAvtomagazinRabbitHealth(
        this IHostApplicationBuilder builder,
        string host,
        string username,
        string password)
    {
        if (builder.Environment.IsEnvironment("Testing"))
        {
            return builder;
        }

        builder.Services.AddHealthChecks()
            .AddCheck("rabbitmq", new RabbitMqHealthCheck(host, username, password), tags: ["ready"]);
        return builder;
    }

    public static WebApplication UseAvtomagazinObservability(this WebApplication app)
    {
        app.UseMiddleware<RequestIdMiddleware>();

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthAsync
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthAsync
        });

        return app;
    }

    private static async Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    durationMs = e.Value.Duration.TotalMilliseconds,
                    description = e.Value.Description,
                    error = e.Value.Exception?.Message
                })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}

public sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[ObservabilityExtensions.RequestIdHeader].ToString();
        var requestId = string.IsNullOrWhiteSpace(incoming) || incoming.Length > 64
            ? Guid.NewGuid().ToString("N")
            : incoming.Trim();

        context.TraceIdentifier = requestId;
        Activity.Current?.SetTag("request.id", requestId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ObservabilityExtensions.RequestIdHeader] = requestId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        {
            await next(context);
        }
    }
}
