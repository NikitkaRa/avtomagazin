using Avtomagazin.ServiceDefaults;
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
builder.AddAvtomagazinForwardedHeaders();

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

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/identity/api/internal"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

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
