using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Contracts.Events;
using Avtomagazin.Identity.Api.Data;
using Avtomagazin.ServiceDefaults;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api;

internal static partial class IdentityRoutes
{
    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        IdentityDbContext db,
        PasswordHasher<AppUser> hasher,
        IConfiguration config,
        IHostEnvironment env)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || !email.Contains('@'))
        {
            return Results.BadRequest(new { error = "Нужен нормальный email" });
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
        {
            return Results.BadRequest(new { error = $"Пароль от {MinPasswordLength} символов" });
        }

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return Results.Conflict(new { error = "Этот email уже зарегистрирован" });
        }

        var client = (request.Client ?? AuthClients.Resident).Trim().ToLowerInvariant();
        string role;
        string status;
        if (client == AuthClients.Staff)
        {
            role = (request.StaffRole ?? "").Trim().ToLowerInvariant();
            if (role is not (Roles.Driver or Roles.Seller or Roles.Operator))
            {
                return Results.BadRequest(new { error = "В приложении персонала укажи роль: водитель, продавец или диспетчер" });
            }

            status = UserStatuses.Pending;
        }
        else if (client is AuthClients.Resident or "")
        {
            role = Roles.Resident;
            status = UserStatuses.Active;
        }
        else
        {
            return Results.BadRequest(new { error = "Неизвестное приложение" });
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? email[..email.IndexOf('@')]
            : request.Name.Trim();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = name,
            Role = role,
            Status = status,
            PasswordHash = "",
            TokenVersion = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = status == UserStatuses.Active ? DateTimeOffset.UtcNow : null
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (status == UserStatuses.Pending)
        {
            return Results.Created($"/api/users/{user.Id}", IdentityMaps.ToLogin(user, accessToken: null));
        }

        return Results.Ok(AuthTokens.Payload(user, config, env));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        IdentityDbContext db,
        PasswordHasher<AppUser> hasher,
        IConfiguration config,
        IHostEnvironment env)
    {
        var email = NormalizeEmail(request.Email);
        var user = email is null
            ? null
            : await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is not null && user.LockoutEndUtc is DateTimeOffset until && until > DateTimeOffset.UtcNow)
        {
            return Results.Json(
                new { error = "Слишком много попыток. Подождите четверть часа." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (user is null
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "")
                == PasswordVerificationResult.Failed)
        {
            if (user is not null)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= MaxFailedLogins)
                {
                    user.LockoutEndUtc = DateTimeOffset.UtcNow.Add(LockoutFor);
                    user.FailedLoginCount = 0;
                }

                await db.SaveChangesAsync();
            }

            return Results.Unauthorized();
        }

        if (user.Status == UserStatuses.Pending)
        {
            return Results.Json(
                new { error = "Админ ещё не подтвердил аккаунт", status = user.Status, role = user.Role },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (user.Status != UserStatuses.Active)
        {
            return Results.Json(
                new { error = "Аккаунт отключён", status = user.Status },
                statusCode: StatusCodes.Status403Forbidden);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync();
        return Results.Ok(AuthTokens.Payload(user, config, env));
    }

    private static async Task<IResult> RefreshAsync(
        ClaimsPrincipal principal,
        IdentityDbContext db,
        IConfiguration config,
        IHostEnvironment env)
    {
        if (principal.UserId() is not Guid id)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.Status != UserStatuses.Active)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(AuthTokens.Payload(user, config, env));
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, IdentityDbContext db)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.UserId() is not Guid id)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(IdentityMaps.ToProfile(user));
    }

    private static async Task<IResult> UpdateProfileAsync(
        [FromBody] UpdateProfileRequest request,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        IObjectStorage storage,
        IPublishEndpoint bus)
    {
        if (principal.UserId() is not Guid id)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.Status != UserStatuses.Active)
        {
            return Results.Unauthorized();
        }

        if (request.LastName is not null)
        {
            user.LastName = TrimOrNull(request.LastName);
        }

        if (request.FirstName is not null)
        {
            user.FirstName = TrimOrNull(request.FirstName);
        }

        if (request.MiddleName is not null)
        {
            user.MiddleName = TrimOrNull(request.MiddleName);
        }

        if (request.Phone is not null)
        {
            user.Phone = TrimOrNull(request.Phone);
        }

        if (request.DisplayName is not null && !string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        user.SyncDisplayName();
        if (string.IsNullOrWhiteSpace(user.DisplayName))
        {
            return Results.BadRequest(new { error = "Укажите хотя бы фамилию или имя" });
        }

        var (photo, photoError) = await MediaPhotos.StoreAsync(
            storage,
            "avatars",
            request.ClearPhoto == true ? null : request.PhotoDataUrl,
            user.PhotoUrl,
            clear: request.ClearPhoto == true);
        if (photoError is not null)
        {
            return Results.BadRequest(new { error = photoError });
        }

        if (request.ClearPhoto == true || request.PhotoDataUrl is not null)
        {
            user.PhotoUrl = photo;
        }

        await db.SaveChangesAsync();
        await bus.Publish(new StaffContactChanged(user.Id, user.DisplayName, user.Phone));
        return Results.Ok(IdentityMaps.ToProfile(user));
    }

    private static async Task<IResult> SessionAsync(Guid id, HttpContext http, IdentityDbContext db, IConfiguration config)
    {
        var expected = config["Internal:Key"];
        if (string.IsNullOrWhiteSpace(expected)
            || !string.Equals(http.Request.Headers["X-Internal-Key"].ToString(), expected, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.Status, u.TokenVersion })
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new { status = user.Status, tokenVersion = user.TokenVersion });
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        PasswordHasher<AppUser> hasher,
        IConfiguration config,
        IHostEnvironment env)
    {
        var userId = principal.UserId();
        var user = userId is Guid id
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == id)
            : null;
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.Current ?? "")
            == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Next) || request.Next.Length < MinPasswordLength)
        {
            return Results.BadRequest(new { error = $"Пароль от {MinPasswordLength} символов" });
        }

        user.PasswordHash = hasher.HashPassword(user, request.Next);
        user.TokenVersion++;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync();
        return Results.Ok(AuthTokens.Payload(user, config, env));
    }
}
