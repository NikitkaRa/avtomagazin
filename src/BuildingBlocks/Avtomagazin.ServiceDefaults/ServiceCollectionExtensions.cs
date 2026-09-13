using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Avtomagazin.ServiceDefaults;

public static class ServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddAvtomagazinDefaults(
        this IHostApplicationBuilder builder,
        Action<IBusRegistrationConfigurator>? configureBus = null)
    {
        builder.AddAvtomagazinObservability();
        builder.Services.AddOpenApi();
        builder.Services.AddMemoryCache();

        var jwtKey = DeploySecrets.JwtKey(builder.Configuration, builder.Environment);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var identityBase = builder.Configuration["Internal:IdentityBaseUrl"];

        if (!string.IsNullOrWhiteSpace(identityBase))
        {
            if (DeploySecrets.IsPublic(builder.Environment) && string.IsNullOrWhiteSpace(builder.Configuration["Internal:Key"]))
            {
                throw new InvalidOperationException("Internal:Key is required when IdentityBaseUrl is set.");
            }

            builder.Services.AddHttpClient<ISessionGuard, HttpSessionGuard>(client =>
            {
                client.BaseAddress = new Uri(identityBase.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(2);
            });
        }
        else if (DeploySecrets.IsPublic(builder.Environment))
        {
            throw new InvalidOperationException("Internal:IdentityBaseUrl is required in Staging/Production.");
        }
        else
        {
            builder.Services.AddSingleton<ISessionGuard, NoopSessionGuard>();
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "avtomagazin",
                    ValidAudience = builder.Configuration["Jwt:Audience"] ?? "avtomagazin",
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events?.OnTokenValidated;
            options.Events ??= new JwtBearerEvents();
            options.Events.OnTokenValidated = async context =>
            {
                if (previous is not null)
                {
                    await previous(context);
                }

                if (context.Principal is null)
                {
                    context.Fail("missing_principal");
                    return;
                }

                var guard = context.HttpContext.RequestServices.GetRequiredService<ISessionGuard>();
                if (!await guard.ValidateAsync(context.Principal, context.HttpContext.RequestAborted))
                {
                    context.Fail("session_revoked");
                }
            };
        });

        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            var testing = builder.Environment.IsEnvironment("Testing");
            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = testing ? 10_000 : 20,
                        QueueLimit = 0
                    }));
        });

        builder.AddAvtomagazinForwardedHeaders();

        var useInMemory =
            builder.Environment.IsEnvironment("Testing")
            || string.Equals(builder.Configuration["Messaging:Transport"], "InMemory", StringComparison.OrdinalIgnoreCase);

        var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

        if (DeploySecrets.IsPublic(builder.Environment)
            && (string.Equals(rabbitPass, "guest", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(rabbitPass)))
        {
            throw new InvalidOperationException("RabbitMq:Password is required in Staging/Production.");
        }

        if (!useInMemory)
        {
            builder.AddAvtomagazinRabbitHealth(rabbitHost, rabbitUser, rabbitPass);
        }

        builder.Services.AddMassTransit(x =>
        {
            configureBus?.Invoke(x);

            if (useInMemory)
            {
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(rabbitHost, "/", h =>
                    {
                        h.Username(rabbitUser);
                        h.Password(rabbitPass);
                    });
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        return builder;
    }

    public static IHostApplicationBuilder AddAvtomagazinForwardedHeaders(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            var local = builder.Environment.IsDevelopment()
                        || builder.Environment.IsEnvironment("Testing");
            if (local)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                return;
            }

            foreach (var raw in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren())
            {
                if (IPAddress.TryParse((raw.Value ?? "").Trim(), out var ip))
                {
                    options.KnownProxies.Add(ip);
                }
            }
        });
        return builder;
    }

    public static WebApplication UseAvtomagazinDefaults(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            await next();
        });
        app.UseAvtomagazinObservability();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
