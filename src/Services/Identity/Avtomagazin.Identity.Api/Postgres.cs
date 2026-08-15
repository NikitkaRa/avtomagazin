using Npgsql;

namespace Avtomagazin.Identity.Api;

internal static class Postgres
{
    public static async Task EnsureDatabaseAsync(string connectionString)
    {
        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        var name = cs.Database;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        cs.Database = "postgres";
        await using var conn = new NpgsqlConnection(cs.ConnectionString);
        await conn.OpenAsync();
        await using (var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn))
        {
            check.Parameters.AddWithValue("n", name);
            if (await check.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        var safe = name.Replace("\"", "");
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{safe}\"", conn);
        await create.ExecuteNonQueryAsync();
    }
}
