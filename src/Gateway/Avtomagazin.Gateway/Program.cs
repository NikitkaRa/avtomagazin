using Avtomagazin.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinObservability();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilder =>
    {
        transformBuilder.AddRequestTransform(context =>
        {
            var requestId = context.HttpContext.TraceIdentifier;
            context.ProxyRequest.Headers.Remove(ObservabilityExtensions.RequestIdHeader);
            context.ProxyRequest.Headers.TryAddWithoutValidation(
                ObservabilityExtensions.RequestIdHeader,
                requestId);
            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
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

app.MapGet("/", () => Results.Ok(new
{
    service = "Avtomagazin.Gateway",
    message = "Use /identity, /fleet, /routing, /notifications prefixes"
}));

app.MapReverseProxy();
app.Run();

public partial class Program;
