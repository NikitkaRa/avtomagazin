using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Avtomagazin.Contracts;
using Avtomagazin.ServiceDefaults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Avtomagazin.IntegrationTests;

internal static class TestJwt
{
    public static readonly Guid GrodnoVan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid PukhovichiVan = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid OzerichinoStop = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd4");

    public static string Mint(string role, Guid? vehicleId = null, Guid? userId = null, int tokenVersion = 1)
    {
        var id = userId ?? Guid.NewGuid();
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DeploySecrets.DemoJwtKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, id.ToString()),
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Role, role),
            new(AuthClaims.TokenVersion, tokenVersion.ToString())
        };
        if (vehicleId is Guid van)
        {
            claims.Add(new Claim(AuthClaims.VehicleId, van.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: "avtomagazin",
            audience: "avtomagazin",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpClient Client<T>(WebApplicationFactory<T> factory, string role, Guid? vehicleId = null, Guid? userId = null)
        where T : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Mint(role, vehicleId, userId));
        return client;
    }
}
