using System.Security.Claims;
using Avtomagazin.Contracts;
using Avtomagazin.Routing.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Avtomagazin.Routing.Api.Endpoints;

public static class CaseEndpoints
{
    public static RouteGroupBuilder MapCaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Cases");

        group.MapGet("/cases", async (string? status, RoutingDbContext db) =>
            {
                var query = db.Cases.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(status))
                {
                    var wanted = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => s.ToLowerInvariant())
                        .ToArray();
                    query = query.Where(c => wanted.Contains(c.Status));
                }
                else
                {
                    query = query.Where(c => c.Status == "open" || c.Status == "in_progress");
                }

                var items = await query
                    .OrderByDescending(c => c.UpdatedAtUtc)
                    .Take(100)
                    .Select(c => new
                    {
                        c.Id,
                        c.SettlementKey,
                        c.SettlementName,
                        c.VehicleId,
                        c.Status,
                        c.ReportCount,
                        c.OpenedAtUtc,
                        c.UpdatedAtUtc,
                        c.ClosedAtUtc
                    })
                    .ToListAsync();
                return Results.Ok(items);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("ListCases");

        group.MapGet("/cases/{id:guid}", async (Guid id, RoutingDbContext db) =>
            {
                var item = await db.Cases.AsNoTracking()
                    .Include(c => c.Events.OrderByDescending(e => e.CreatedAtUtc))
                    .FirstOrDefaultAsync(c => c.Id == id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("GetCase");

        group.MapPost("/cases/{id:guid}/comments", async (
                Guid id,
                CaseCommentRequest request,
                ClaimsPrincipal principal,
                RoutingDbContext db) =>
            {
                var body = (request.Body ?? "").Trim();
                if (body.Length == 0)
                {
                    return Results.BadRequest(new { error = "Нужен текст комментария" });
                }

                var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == id);
                if (item is null)
                {
                    return Results.NotFound();
                }

                var now = DateTimeOffset.UtcNow;
                db.CaseEvents.Add(new CaseEvent
                {
                    Id = Guid.NewGuid(),
                    CaseId = item.Id,
                    Kind = "comment",
                    Body = body,
                    AuthorName = principal.FindFirstValue("name") ?? principal.Identity?.Name,
                    AuthorEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                    CreatedAtUtc = now
                });
                item.UpdatedAtUtc = now;
                await db.SaveChangesAsync();
                return Results.Ok(await CaseWorkflow.LoadAsync(db, item.Id));
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("AddCaseComment");

        group.MapPatch("/cases/{id:guid}/status", async (
                Guid id,
                CaseStatusRequest request,
                ClaimsPrincipal principal,
                RoutingDbContext db) =>
            {
                var status = (request.Status ?? "").Trim().ToLowerInvariant();
                if (status is not ("open" or "in_progress" or "closed"))
                {
                    return Results.BadRequest(new { error = "status: open, in_progress или closed" });
                }

                var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == id);
                if (item is null)
                {
                    return Results.NotFound();
                }

                if (item.Status == status && string.IsNullOrWhiteSpace(request.Comment))
                {
                    return Results.Ok(await CaseWorkflow.LoadAsync(db, item.Id));
                }

                var now = DateTimeOffset.UtcNow;
                var from = item.Status;
                item.Status = status;
                item.UpdatedAtUtc = now;
                item.ClosedAtUtc = status == "closed" ? now : null;

                var label = status switch
                {
                    "open" => "открыта",
                    "in_progress" => "в процессе",
                    "closed" => "закрыта",
                    _ => status
                };
                var note = string.IsNullOrWhiteSpace(request.Comment)
                    ? $"Статус: {label}"
                    : $"Статус: {label}. {request.Comment.Trim()}";

                db.CaseEvents.Add(new CaseEvent
                {
                    Id = Guid.NewGuid(),
                    CaseId = item.Id,
                    Kind = "status",
                    Body = note,
                    AuthorName = principal.FindFirstValue("name") ?? principal.Identity?.Name,
                    AuthorEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                    FromStatus = from,
                    ToStatus = status,
                    CreatedAtUtc = now
                });

                await db.SaveChangesAsync();
                return Results.Ok(await CaseWorkflow.LoadAsync(db, item.Id));
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("SetCaseStatus");

        return group;
    }
}
