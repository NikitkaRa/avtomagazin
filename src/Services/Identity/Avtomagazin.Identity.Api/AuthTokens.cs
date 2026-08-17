using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Avtomagazin.Contracts;
using Avtomagazin.Identity.Api.Data;
using Avtomagazin.ServiceDefaults;
using Microsoft.IdentityModel.Tokens;

namespace Avtomagazin.Identity.Api;

internal static class AuthTokens
{
    public static LoginResponse Payload(AppUser user, IConfiguration config, IHostEnvironment env)
    {
        var key = DeploySecrets.JwtKey(config, env);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("name", user.DisplayName),
            new(AuthClaims.TokenVersion, user.TokenVersion.ToString())
        };

        if (user.VehicleId is Guid vehicleId)
        {
            claims.Add(new Claim(AuthClaims.VehicleId, vehicleId.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"] ?? "avtomagazin",
            audience: config["Jwt:Audience"] ?? "avtomagazin",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return IdentityMaps.ToLogin(user, new JwtSecurityTokenHandler().WriteToken(token));
    }
}
