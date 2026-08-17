using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Avtomagazin.ServiceDefaults;

public static class RelationalSchema
{
    public static async Task ApplyAsync(DbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

        if (await NeedsBaselineAsync(db, ct))
        {
            throw new InvalidOperationException(
                "Database has tables but no EF migration history. Restore __EFMigrationsHistory or migrate from a clean database. Automatic baseline is disabled.");
        }

        await db.Database.MigrateAsync(ct);
    }

    private static async Task<bool> NeedsBaselineAsync(DbContext db, CancellationToken ct)
    {
        var history = db.GetService<IHistoryRepository>();
        if (history.Exists() && history.GetAppliedMigrations().Count > 0)
        {
            return false;
        }

        await db.Database.OpenConnectionAsync(ct);
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_type = 'BASE TABLE'
                  AND table_name <> '__EFMigrationsHistory'
            )
            """;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }
}
