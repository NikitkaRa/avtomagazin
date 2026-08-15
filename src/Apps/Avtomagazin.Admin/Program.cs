using Avtomagazin.Admin.Components;
using Avtomagazin.ApiClient;
using Avtomagazin.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinObservability();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<Avtomagazin.Admin.StaffSession>();
builder.Services.AddScoped<Avtomagazin.ApiClient.IAccessTokenAccessor>(sp =>
    sp.GetRequiredService<Avtomagazin.Admin.StaffSession>());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var gateway = builder.Configuration["Gateway:BaseUrl"]
              ?? Avtomagazin.ApiClient.AvtomagazinEnvironments.GatewayUrl(builder.Environment.EnvironmentName);
builder.Services.AddAvtomagazinApiClient(gateway);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseAvtomagazinObservability();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
