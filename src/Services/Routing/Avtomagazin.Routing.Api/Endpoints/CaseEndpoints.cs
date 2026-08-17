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
                    query = query.Where(c => c.Status == CaseStatuses.Open || c.Status == CaseStatuses.InProgress);
                }

                var items = await query
                    .OrderByDescending(c => c.UpdatedAtUtc)
                    .Take(100)
                    .ToListAsync();
                return Results.Ok(items.Select(c => c.ToSummary()));
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Driver, Roles.Seller, Roles.Operator, Roles.Admin))
            .WithName("ListCases");

        group.MapGet("/cases/{id:guid}", async (Guid id, RoutingDbContext db) =>
            {
                var item = await db.Cases.AsNoTracking()
                    .Include(c => c.Events.OrderByDescending(e => e.CreatedAtUtc))
                    .FirstOrDefaultAsync(c => c.Id == id);
                return item is null ? Results.NotFound() : Results.Ok(item.ToDetail());
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
                    Kind = CaseEventKinds.Comment,
                    Body = body,
                    AuthorName = principal.FindFirstValue("name") ?? principal.Identity?.Name,
                    AuthorEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                    CreatedAtUtc = now
                });
                item.UpdatedAtUtc = now;
                await db.SaveChangesAsync();
                return Results.Ok((await CaseWorkflow.LoadAsync(db, item.Id))?.ToDetail());
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
                if (!CaseStatuses.IsKnown(status))
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
                    return Results.Ok((await CaseWorkflow.LoadAsync(db, item.Id))?.ToDetail());
                }

                var now = DateTimeOffset.UtcNow;
                var from = item.Status;
                item.Status = status;
                item.UpdatedAtUtc = now;
                item.ClosedAtUtc = status == CaseStatuses.Closed ? now : null;

                var label = CaseStatuses.Title(status);
                var note = string.IsNullOrWhiteSpace(request.Comment)
                    ? $"Статус: {label}"
                    : $"Статус: {label}. {request.Comment.Trim()}";

                db.CaseEvents.Add(new CaseEvent
                {
                    Id = Guid.NewGuid(),
                    CaseId = item.Id,
                    Kind = CaseEventKinds.Status,
                    Body = note,
                    AuthorName = principal.FindFirstValue("name") ?? principal.Identity?.Name,
                    AuthorEmail = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                    FromStatus = from,
                    ToStatus = status,
                    CreatedAtUtc = now
                });

                await db.SaveChangesAsync();
                return Results.Ok((await CaseWorkflow.LoadAsync(db, item.Id))?.ToDetail());
            })
            .RequireAuthorization(policy => policy.RequireRole(Roles.Operator, Roles.Admin))
            .WithName("SetCaseStatus");

        return group;
    }
}
