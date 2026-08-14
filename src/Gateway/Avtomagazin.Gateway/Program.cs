var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    service = "Avtomagazin.Gateway",
    message = "Use /identity, /fleet, /routing, /notifications prefixes",
    openApi = "/openapi/v1.json"
}));

app.MapReverseProxy();
app.Run();

public partial class Program;
