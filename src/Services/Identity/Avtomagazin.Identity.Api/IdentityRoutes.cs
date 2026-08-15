using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Identity.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Identity.Api;

internal static class IdentityRoutes
{
    public const int MinPasswordLength = 8;
    private const int MaxFailedLogins = 8;
    private static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/register", RegisterAsync)
            .RequireRateLimiting("auth")
            .WithName("Register")
            .WithTags("Auth");
        app.MapPost("/api/auth/login", LoginAsync)
            .RequireRateLimiting("auth")
            .WithName("Login")
            .WithTags("Auth");
        app.MapGet("/api/auth/me", Me).RequireAuthorization().WithName("Me").WithTags("Auth");
        app.MapGet("/api/internal/users/{id:guid}/session", SessionAsync)
            .WithName("InternalSession")
            .WithTags("Internal");

        app.MapGet("/api/users", ListUsersAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("ListUsers")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("ApproveUser")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("RejectUser")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/disable", DisableAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("DisableUser")
            .WithTags("Users");
        app.MapPost("/api/users/{id:guid}/password", ResetPasswordAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.Admin))
            .WithName("ResetPassword")
            .WithTags("Users");
        app.MapPost("/api/auth/password", ChangePasswordAsync)
            .RequireAuthorization()
            .WithName("ChangePassword")
            .WithTags("Auth");
    }

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
            if (role is not (Roles.Driver or Roles.Operator))
            {
                return Results.BadRequest(new { error = "В приложении персонала укажи роль: водитель или диспетчер" });
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
            return Results.Created($"/api/users/{user.Id}", new
            {
                accessToken = (string?)null,
                role = user.Role,
                userId = user.Id,
                email = user.Email,
                name = user.DisplayName,
                vehicleId = user.VehicleId,
                status = user.Status
            });
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

    private static IResult Me(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            id = principal.UserId(),
            email = principal.FindFirstValue(JwtRegisteredClaimNames.Email)
                    ?? principal.FindFirstValue(ClaimTypes.Email),
            role = principal.Role(),
            name = principal.FindFirstValue("name"),
            vehicleId = principal.AssignedVehicleId()
        });
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

    private static async Task<IResult> ListUsersAsync(string? status, IdentityDbContext db)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status == status);
        }

        var items = await query
            .OrderBy(u => u.Status == UserStatuses.Pending ? 0 : 1)
            .ThenBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Role,
                roleTitle = Roles.Title(u.Role),
                u.Status,
                u.VehicleId,
                u.CreatedAtUtc,
                u.ApprovedAtUtc
            })
            .ToListAsync();

        return Results.Ok(items);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        [FromBody] ApproveUserRequest? request,
        IdentityDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Role == Roles.Admin)
        {
            return Results.BadRequest(new { error = "Админа так не трогаем" });
        }

        var role = (request?.Role ?? user.Role).Trim().ToLowerInvariant();
        if (role is not (Roles.Driver or Roles.Operator or Roles.Resident))
        {
            return Results.BadRequest(new { error = "Роль: resident, driver или operator" });
        }

        if (request?.VehicleId is Guid van)
        {
            user.VehicleId = van == Guid.Empty ? null : van;
        }

        if (role == Roles.Driver && user.VehicleId is null)
        {
            return Results.BadRequest(new { error = "Водителю нужна автолавка" });
        }

        var wasDisabled = user.Status == UserStatuses.Disabled;
        user.Role = role;
        user.Status = UserStatuses.Active;
        user.ApprovedAtUtc = DateTimeOffset.UtcNow;
        if (wasDisabled)
        {
            user.TokenVersion++;
        }

        await db.SaveChangesAsync();
        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role,
            roleTitle = Roles.Title(user.Role),
            user.Status,
            user.VehicleId
        });
    }

    private static async Task<IResult> RejectAsync(Guid id, IdentityDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Status != UserStatuses.Pending)
        {
            return Results.BadRequest(new { error = "Отклонить можно только заявку" });
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DisableAsync(Guid id, IdentityDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Role == Roles.Admin)
        {
            return Results.BadRequest(new { error = "Админа так не трогаем" });
        }

        user.Status = UserStatuses.Disabled;
        user.TokenVersion++;
        await db.SaveChangesAsync();
        return Results.Ok(new { user.Id, user.Status });
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

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        [FromBody] SetPasswordRequest request,
        IdentityDbContext db,
        PasswordHasher<AppUser> hasher)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.Role == Roles.Admin)
        {
            return Results.BadRequest(new { error = "Админа так не трогаем" });
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
        {
            return Results.BadRequest(new { error = $"Пароль от {MinPasswordLength} символов" });
        }

        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.TokenVersion++;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync();
        return Results.Ok(new { user.Id, user.Status });
    }

    private static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}

internal sealed record LoginRequest(string Email, string Password);

internal sealed record RegisterRequest(
    string Email,
    string Password,
    string? Name,
    string? Client,
    string? StaffRole);

internal sealed record ApproveUserRequest(string? Role, Guid? VehicleId);

internal sealed record ChangePasswordRequest(string Current, string Next);

internal sealed record SetPasswordRequest(string Password);
