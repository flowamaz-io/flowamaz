using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.EgressProxy;

/// <summary>
/// Egress proxy for the connector sandbox. Configuration (env):
///   Proxy__Port=3128                    listen port
///   Proxy__AllowedPorts=443,80          destination ports the sandbox may reach
///   Proxy__AllowPrivateNetworks=false   NEVER true on Cloud
/// (Explicit entry class rather than top-level statements so it never collides with the runner's
/// global <c>Program</c> in test assemblies that reference both.)
/// </summary>
internal static class EgressProxyProgram
{
    public static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
        using var loggerFactory = LoggerFactory.Create(b => b.AddJsonConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger("EgressProxy");

        var options = new EgressProxyOptions
        {
            Port = int.TryParse(config["Proxy:Port"], out var port) ? port : 3128,
            AllowedPorts = ParsePorts(config["Proxy:AllowedPorts"]),
            AllowPrivateNetworks = bool.TryParse(config["Proxy:AllowPrivateNetworks"], out var allow) && allow,
        };
        if (options.AllowedPorts.Count == 0)
        {
            logger.LogCritical("Proxy:AllowedPorts has no valid ports. Set e.g. Proxy__AllowedPorts=443,80.");
            return 1;
        }

        await using var server = new EgressProxyServer(options, logger);
        server.Start();

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Cancel();
        try { await Task.Delay(Timeout.Infinite, shutdown.Token); } catch (OperationCanceledException) { }
        return 0;
    }

    internal static IReadOnlySet<int> ParsePorts(string? value) =>
        (value ?? "443,80")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var n) ? n : -1)
            .Where(n => n is > 0 and < 65536)
            .ToHashSet();
}
