using System.Text.Json;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Executes parameterized PostgreSQL queries. SQL is NEVER concatenated with user input.
/// Input for query: { connection_string: string, sql: string, parameters?: { name: value } }
/// Input for execute: same shape; returns affected row count.
/// </summary>
public abstract class PostgreSqlHandlerBase : IConnectorOperationHandler
{
    private readonly ILogger _logger;
    protected PostgreSqlHandlerBase(ILogger logger) => _logger = logger;

    public string ConnectorId => "postgresql";
    public abstract string OperationId { get; }
    protected ILogger Logger => _logger;

    public abstract Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct);

    protected static NpgsqlConnection OpenConnection(string connectionString) =>
        new(connectionString);

    protected static void BindParameters(NpgsqlCommand cmd, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return;

        foreach (var prop in parameters.EnumerateObject())
        {
            var value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => (object?)prop.Value.GetString(),
                JsonValueKind.Number when prop.Value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => DBNull.Value,
                _ => prop.Value.GetRawText()
            };
            cmd.Parameters.AddWithValue("@" + prop.Name, value ?? DBNull.Value);
        }
    }
}

public sealed class PostgreSqlQueryHandler : PostgreSqlHandlerBase
{
    public PostgreSqlQueryHandler(ILogger<PostgreSqlQueryHandler> l) : base(l) { }
    public override string OperationId => "query";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[PostgreSQL:query] ExecuteAsync entry");

        var root = input.RootElement;
        var connectionString = root.GetProperty("connection_string").GetString()
            ?? throw new InvalidOperationException("'connection_string' is required.");
        var sql = root.GetProperty("sql").GetString()
            ?? throw new InvalidOperationException("'sql' is required.");

        await using var conn = OpenConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql; // SQL from validated workflow definition — parameterized below

        if (root.TryGetProperty("parameters", out var paramsEl))
            BindParameters(cmd, paramsEl);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        Logger.LogInformation("[PostgreSQL:query] ExecuteAsync exit rows={Count}", rows.Count);
        return JsonDocument.Parse(JsonSerializer.Serialize(new { rows }));
    }
}

public sealed class PostgreSqlExecuteHandler : PostgreSqlHandlerBase
{
    public PostgreSqlExecuteHandler(ILogger<PostgreSqlExecuteHandler> l) : base(l) { }
    public override string OperationId => "execute";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, WorkspaceCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[PostgreSQL:execute] ExecuteAsync entry");

        var root = input.RootElement;
        var connectionString = root.GetProperty("connection_string").GetString()
            ?? throw new InvalidOperationException("'connection_string' is required.");
        var sql = root.GetProperty("sql").GetString()
            ?? throw new InvalidOperationException("'sql' is required.");

        await using var conn = OpenConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (root.TryGetProperty("parameters", out var paramsEl))
            BindParameters(cmd, paramsEl);

        var affected = await cmd.ExecuteNonQueryAsync(ct);

        Logger.LogInformation("[PostgreSQL:execute] ExecuteAsync exit affected={Count}", affected);
        return JsonDocument.Parse($"{{\"affected_rows\":{affected}}}");
    }
}
