using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MITAssetAgent.Core.Models;

namespace MITAssetAgent.Core.Services;

public interface IOfflineQueueService
{
    void Enqueue(string payloadJson);
    IReadOnlyList<(long Id, string Payload)> PeekBatch(int take = 20);
    void Remove(long id);
    int Count();
}

public sealed class SqliteOfflineQueueService : IOfflineQueueService
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteOfflineQueueService> _log;

    public SqliteOfflineQueueService(IOptions<AgentOptions> options, ILogger<SqliteOfflineQueueService> log)
    {
        _log = log;
        Directory.CreateDirectory(options.Value.DataDirectory);
        _dbPath = Path.Combine(options.Value.DataDirectory, "agent.db");
        Init();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        return c;
    }

    private void Init()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS heartbeat_queue (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              payload TEXT NOT NULL,
              created_at TEXT NOT NULL,
              attempts INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void Enqueue(string payloadJson)
    {
        // Deduplicate: keep at most one pending identical payload in the last hour
        using var c = Open();
        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM heartbeat_queue WHERE payload = $p AND created_at > $since";
            check.Parameters.AddWithValue("$p", payloadJson);
            check.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
            var exists = Convert.ToInt32(check.ExecuteScalar());
            if (exists > 0) return;
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO heartbeat_queue(payload, created_at) VALUES ($p, $t)";
        cmd.Parameters.AddWithValue("$p", payloadJson);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        _log.LogInformation("Queued offline heartbeat (queue size {N})", Count());
    }

    public IReadOnlyList<(long Id, string Payload)> PeekBatch(int take = 20)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, payload FROM heartbeat_queue ORDER BY id LIMIT $n";
        cmd.Parameters.AddWithValue("$n", take);
        var list = new List<(long, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    public void Remove(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM heartbeat_queue WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public int Count()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM heartbeat_queue";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
