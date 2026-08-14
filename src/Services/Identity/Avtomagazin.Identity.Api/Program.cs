using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Avtomagazin.Contracts;
using Avtomagazin.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.AddAvtomagazinDefaults();

var app = builder.Build();
app.UseAvtomagazinDefaults();

var grodnoVan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
var pukhovichiVan = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
var users = new List<UserRecord>
{
    new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "resident@demo.by", "Житель", Roles.Resident, "demo", null),
    new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "driver@demo.by", "Водитель Гродно", Roles.Driver, "demo", grodnoVan),
    new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "seller@demo.by", "Водитель Озеричино", Roles.Driver, "demo", pukhovichiVan),
    new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "operator@demo.by", "Оператор", Roles.Operator, "demo", null),
    new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "admin@demo.by", "Админ", Roles.Admin, "demo", null)
};

app.MapPost("/api/auth/login", ([FromBody] LoginRequest request, IConfiguration config) =>
{
    var user = users.FirstOrDefault(u =>
        u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)
        && u.Password == request.Password);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var key = config["Jwt:Key"] ?? "dev-only-change-me-avtomagazin-super-secret-key-32b";
    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim("name", user.DisplayName)
    };

    if (user.VehicleId is Guid vehicleId)
    {
        claims = [.. claims, new Claim("vehicleId", vehicleId.ToString())];
    }

    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"] ?? "avtomagazin",
        audience: config["Jwt:Audience"] ?? "avtomagazin",
        claims: claims,
        expires: DateTime.UtcNow.AddHours(12),
        signingCredentials: credentials);

    return Results.Ok(new
    {
        accessToken = new JwtSecurityTokenHandler().WriteToken(token),
        role = user.Role,
        userId = user.Id,
        email = user.Email,
        name = user.DisplayName,
        vehicleId = user.VehicleId
    });
})
.WithName("Login")
.WithTags("Auth");

app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        id = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
        email = user.FindFirstValue(JwtRegisteredClaimNames.Email) ?? user.FindFirstValue(ClaimTypes.Email),
        role = user.FindFirstValue(ClaimTypes.Role),
        name = user.FindFirstValue("name")
    });
})
.RequireAuthorization()
.WithName("Me")
.WithTags("Auth");

app.Run();

internal sealed record LoginRequest(string Email, string Password);
internal sealed record UserRecord(Guid Id, string Email, string DisplayName, string Role, string Password, Guid? VehicleId);

public partial class Program;
