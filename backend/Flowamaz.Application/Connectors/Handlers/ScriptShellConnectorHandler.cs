using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// Runs a bash or python3 script. This is arbitrary code execution on the host, so it is OFF unless
/// <c>Connectors:ScriptShell:Enabled=true</c> — intended for self-hosted installs only. FUNCTIONAL.md
/// §9.5 container isolation does not exist yet, so Cloud must keep this disabled.
///
/// Guard rails when enabled: interpreter allowlist; the script goes on stdin and arguments through
/// ArgumentList (never a shell command string); environment cleared to PATH/LANG/HOME; a per-workspace
/// working directory; timeout kills the whole process tree; stdout/stderr capped at 64 KB each.
/// Input: { interpreter: bash|python3, script, args?: [], timeout_seconds? }.
/// Output: { exit_code, stdout, stderr, truncated }. A non-zero exit fails the step.
/// </summary>
public sealed class ScriptShellRunHandler : IConnectorOperationHandler
{
    public const string EnabledKey = "Connectors:ScriptShell:Enabled";
    private const int MaxOutputChars = 64 * 1024;

    private static readonly Dictionary<string, (string File, string[] StdinArgs)> Interpreters = new()
    {
        ["bash"] = ("bash", ["-s", "--"]),
        ["python3"] = ("python3", ["-"]),
    };

    private readonly IConfiguration _configuration;
    private readonly ConnectorCallContext _context;
    private readonly ILogger<ScriptShellRunHandler> _logger;

    public ScriptShellRunHandler(IConfiguration configuration, ConnectorCallContext context, ILogger<ScriptShellRunHandler> logger)
    {
        _configuration = configuration;
        _context = context;
        _logger = logger;
    }

    public string ConnectorId => "script-shell";
    public string OperationId => "run-script";

    public async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        const string op = "script-shell/run-script";
        _logger.LogInformation("[ScriptShell:run-script] ExecuteAsync entry");

        if (!_configuration.GetValue<bool>(EnabledKey))
            throw new ConnectorOperationException(
                $"{op}: the Script/Shell connector is disabled on this installation because it runs code on the server. " +
                $"A self-hosted admin can enable it with {EnabledKey}=true.");

        var root = input.RootElement;
        var interpreterName = ConnectorInput.Required(root, "interpreter", op).Trim().ToLowerInvariant();
        if (!Interpreters.TryGetValue(interpreterName, out var interpreter))
            throw new ConnectorOperationException($"{op}: interpreter must be one of: {string.Join(", ", Interpreters.Keys)}.");
        var script = ConnectorInput.Required(root, "script", op);
        var timeout = TimeSpan.FromSeconds(ConnectorInput.Int(root, "timeout_seconds", 30, 1, 300));
        var args = root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText()).ToArray()
            : [];

        var workDir = Path.Combine(Path.GetTempPath(), "flowamaz-scripts", _context.RequireWorkspace(op).ToString("N"));
        Directory.CreateDirectory(workDir);

        var psi = new ProcessStartInfo(interpreter.File)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        };
        foreach (var arg in interpreter.StdinArgs.Concat(args)) psi.ArgumentList.Add(arg);
        psi.Environment.Clear();
        psi.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        psi.Environment["LANG"] = "C.UTF-8";
        psi.Environment["HOME"] = workDir;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var truncatedFlag = new bool[1]; // captured by the output callbacks
        process.OutputDataReceived += (_, e) => Append(stdout, e.Data, truncatedFlag);
        process.ErrorDataReceived += (_, e) => Append(stderr, e.Data, truncatedFlag);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new ConnectorOperationException($"{op}: '{interpreter.File}' is not installed on this server.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
            if (ct.IsCancellationRequested) throw;
            throw new ConnectorOperationException($"{op}: the script did not finish within {timeout.TotalSeconds:0}s and was stopped.");
        }

        process.WaitForExit(); // flush async output handlers
        _logger.LogInformation("[ScriptShell:run-script] ExecuteAsync exit code={Code}", process.ExitCode);

        if (process.ExitCode != 0)
        {
            var tail = stderr.Length > 500 ? stderr.ToString()[^500..] : stderr.ToString();
            throw new ConnectorOperationException($"{op}: the script exited with code {process.ExitCode}. stderr: {tail}");
        }

        return ConnectorHttp.ToDocument(new
        {
            exit_code = process.ExitCode,
            stdout = stdout.ToString(),
            stderr = stderr.ToString(),
            truncated = truncatedFlag[0],
        });
    }

    private static void Append(StringBuilder sb, string? line, bool[] truncated)
    {
        if (line is null) return;
        lock (sb)
        {
            if (sb.Length + line.Length + 1 > MaxOutputChars) { truncated[0] = true; return; }
            sb.AppendLine(line);
        }
    }
}
