using Avtomagazin.Admin.Components;
using Avtomagazin.ApiClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<Avtomagazin.Admin.StaffSession>();
builder.Services.AddScoped<Avtomagazin.ApiClient.IAccessTokenAccessor>(sp =>
    sp.GetRequiredService<Avtomagazin.Admin.StaffSession>());

var gateway = builder.Configuration["Gateway:BaseUrl"] ?? "http://127.0.0.1:5100";
builder.Services.AddAvtomagazinApiClient(gateway);

var app = builder.Build();

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
