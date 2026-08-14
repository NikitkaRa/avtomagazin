using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
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
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks();

        var jwtKey = builder.Configuration["Jwt:Key"]
                     ?? "dev-only-change-me-avtomagazin-super-secret-key-32b";
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

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
                    IssuerSigningKey = signingKey
                };
            });

        builder.Services.AddAuthorization();

        var useInMemory =
            builder.Environment.IsEnvironment("Testing")
            || string.Equals(builder.Configuration["Messaging:Transport"], "InMemory", StringComparison.OrdinalIgnoreCase);

        var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

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

    public static WebApplication UseAvtomagazinDefaults(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapHealthChecks("/health");
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
