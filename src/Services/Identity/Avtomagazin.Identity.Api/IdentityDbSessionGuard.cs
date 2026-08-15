using Avtomagazin.Contracts;
using Avtomagazin.Identity.Api.Data;
using Avtomagazin.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api;

internal sealed class IdentityDbSessionGuard(IdentityDbContext db) : ISessionGuard
{
    public async Task<bool> ValidateAsync(System.Security.Claims.ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var userId = principal.UserId();
        if (userId is null)
        {
            return false;
        }

        var user = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.Status, u.TokenVersion })
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is not null
               && user.Status == UserStatuses.Active
               && user.TokenVersion == principal.TokenVersion();
    }
}
