using System.Data.Common;
using System.Net;
using System.Text.Json;
using Flowamaz.Application.Connectors.Security;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Parameterized SQL for PostgreSQL and MySQL. The connection string comes only from the workspace
/// credential (AuthType ConnectionString) — never from workflow inputs. Values are bound as
/// parameters, never concatenated. Results are capped at <see cref="MaxRows"/> rows; the database
/// host is subject to the same private-network policy as HTTP egress.
/// Input: { sql, parameters?: { name: value } }.
/// </summary>
public abstract class SqlHandlerBase : IConnectorOperationHandler
{
    public const int MaxRows = 1000;
    private const int CommandTimeoutSeconds = 30;

    private readonly bool _allowPrivateNetworks;

    protected SqlHandlerBase(IConfiguration configuration, ILogger logger)
    {
        _allowPrivateNetworks = configuration.GetValue<bool>(EgressGuard.ConfigKey);
        Logger = logger;
    }

    protected ILogger Logger { get; }
    public abstract string ConnectorId { get; }
    public abstract string OperationId { get; }
    protected abstract bool IsQuery { get; }
    protected abstract string DisplayName { get; }

    protected abstract (DbConnection Connection, string Host) CreateConnection(string connectionString);

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        var op = $"{ConnectorId}/{OperationId}";
        Logger.LogInformation("[{Op}] ExecuteAsync entry", op);

        var root = input.RootElement;
        ConnectorInput.RejectSecretInput(root, op, "connection_string", "password");
        var cred = ConnectorInput.RequireCredential(credential, DisplayName, op);
        var sql = ConnectorInput.Required(root, "sql", op);

        DbConnection connection;
        string host;
        try
        {
            (connection, host) = CreateConnection(cred.Secret);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new ConnectorOperationException(
                $"{op}: the connection string in credential '{cred.Name}' is invalid. Update it in Settings → Credentials.");
        }

        await EnsureHostAllowedAsync(host, op, ct);

        await using (connection)
        {
            try
            {
                await connection.OpenAsync(ct);
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = CommandTimeoutSeconds;
                if (root.TryGetProperty("parameters", out var parameters)) BindParameters(cmd, parameters);

                JsonDocument result;
                if (IsQuery)
                {
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    var rows = new List<Dictionary<string, object?>>();
                    var truncated = false;
                    while (await reader.ReadAsync(ct))
                    {
                        if (rows.Count == MaxRows) { truncated = true; break; }
                        var row = new Dictionary<string, object?>(reader.FieldCount);
                        for (var i = 0; i < reader.FieldCount; i++)
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : ToJsonSafe(reader.GetValue(i));
                        rows.Add(row);
                    }
                    result = ConnectorHttp.ToDocument(new { rows, row_count = rows.Count, truncated });
                    Logger.LogInformation("[{Op}] ExecuteAsync exit rows={Count} truncated={Truncated}", op, rows.Count, truncated);
                }
                else
                {
                    var affected = await cmd.ExecuteNonQueryAsync(ct);
                    result = ConnectorHttp.ToDocument(new { affected_rows = affected });
                    Logger.LogInformation("[{Op}] ExecuteAsync exit affected={Count}", op, affected);
                }

                return result;
            }
            catch (DbException ex)
            {
                // Database messages describe the SQL problem; they never include the connection string.
                throw new ConnectorOperationException($"{op}: {DisplayName} error: {ex.Message}", retryable: ex.IsTransient, ex);
            }
        }
    }

    private async Task EnsureHostAllowedAsync(string host, string op, CancellationToken ct)
    {
        if (_allowPrivateNetworks || string.IsNullOrWhiteSpace(host)) return;
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            throw new ConnectorOperationException($"{op}: could not resolve database host '{host}'. Check the connection string.", retryable: true);
        }

        if (addresses.Length == 0 || addresses.All(EgressGuard.IsBlocked))
            throw new ConnectorOperationException(
                $"{op}: database host '{host}' is on a private or internal network, which hosted workflows cannot reach. " +
                "Use a publicly reachable endpoint, or enable Connectors:AllowPrivateNetworkEgress on a self-hosted install.");
    }

    private static void BindParameters(DbCommand cmd, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in parameters.EnumerateObject())
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@" + prop.Name;
            p.Value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => (object?)prop.Value.GetString(),
                JsonValueKind.Number when prop.Value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => prop.Value.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => DBNull.Value,
                _ => prop.Value.GetRawText(),
            } ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }

    private static object? ToJsonSafe(object value) => value switch
    {
        byte[] bytes => Convert.ToBase64String(bytes),
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        Guid g => g.ToString(),
        TimeSpan ts => ts.ToString(),
        _ => value,
    };
}

public abstract class PostgreSqlHandlerBase : SqlHandlerBase
{
    protected PostgreSqlHandlerBase(IConfiguration c, ILogger l) : base(c, l) { }
    public override string ConnectorId => "postgresql";
    protected override string DisplayName => "PostgreSQL";

    protected override (DbConnection, string) CreateConnection(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return (new NpgsqlConnection(builder.ConnectionString), builder.Host ?? string.Empty);
    }
}

public sealed class PostgreSqlQueryHandler : PostgreSqlHandlerBase
{
    public PostgreSqlQueryHandler(IConfiguration c, ILogger<PostgreSqlQueryHandler> l) : base(c, l) { }
    public override string OperationId => "query";
    protected override bool IsQuery => true;
}

public sealed class PostgreSqlExecuteHandler : PostgreSqlHandlerBase
{
    public PostgreSqlExecuteHandler(IConfiguration c, ILogger<PostgreSqlExecuteHandler> l) : base(c, l) { }
    public override string OperationId => "execute";
    protected override bool IsQuery => false;
}

public abstract class MySqlHandlerBase : SqlHandlerBase
{
    protected MySqlHandlerBase(IConfiguration c, ILogger l) : base(c, l) { }
    public override string ConnectorId => "mysql";
    protected override string DisplayName => "MySQL";

    protected override (DbConnection, string) CreateConnection(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            // Never let a query read files from the client machine.
            AllowLoadLocalInfile = false,
        };
        return (new MySqlConnection(builder.ConnectionString), builder.Server ?? string.Empty);
    }
}

public sealed class MySqlQueryHandler : MySqlHandlerBase
{
    public MySqlQueryHandler(IConfiguration c, ILogger<MySqlQueryHandler> l) : base(c, l) { }
    public override string OperationId => "query";
    protected override bool IsQuery => true;
}

public sealed class MySqlExecuteHandler : MySqlHandlerBase
{
    public MySqlExecuteHandler(IConfiguration c, ILogger<MySqlExecuteHandler> l) : base(c, l) { }
    public override string OperationId => "execute";
    protected override bool IsQuery => false;
}
