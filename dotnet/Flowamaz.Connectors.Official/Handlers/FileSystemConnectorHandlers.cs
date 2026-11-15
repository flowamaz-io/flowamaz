using System.Text;
using System.Text.Json;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Flowamaz.Application.Connectors.Handlers;

/// <summary>
/// File system connector. Each workspace gets its own folder under <c>Connectors:FileSystem:RootPath</c>;
/// paths are resolved inside it and anything escaping it (.., absolute paths, symlinks) is rejected.
/// Disabled when the root path is not configured (Cloud default). Text files only, 1 MB per file.
/// </summary>
public abstract class FileSystemHandlerBase : IConnectorOperationHandler
{
    public const string RootPathKey = "Connectors:FileSystem:RootPath";
    protected const int MaxBytes = 1024 * 1024;

    private readonly string? _rootPath;
    private readonly ConnectorCallContext _context;

    protected FileSystemHandlerBase(IConfiguration configuration, ConnectorCallContext context, ILogger logger)
    {
        _rootPath = configuration[RootPathKey];
        _context = context;
        Logger = logger;
    }

    protected ILogger Logger { get; }
    public string ConnectorId => "file-system";
    public abstract string OperationId { get; }
    protected string Op => $"file-system/{OperationId}";

    public abstract Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct);

    /// <summary>The workspace's folder, created on first use.</summary>
    protected string WorkspaceRoot()
    {
        if (string.IsNullOrWhiteSpace(_rootPath))
            throw new ConnectorOperationException(
                $"{Op}: the File System connector is not enabled on this installation. A self-hosted admin can enable it by setting {RootPathKey}.");
        var workspaceId = _context.RequireWorkspace(Op);
        var root = Path.GetFullPath(Path.Combine(_rootPath, workspaceId.ToString("N")));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Resolves a user path inside the workspace root, rejecting traversal and symlinks.</summary>
    internal string Resolve(string root, string? relative)
    {
        var rel = (relative ?? string.Empty).Replace('\\', '/').Trim();
        if (Path.IsPathRooted(rel) || rel.Split('/').Any(s => s == ".."))
            throw new ConnectorOperationException($"{Op}: path '{relative}' must be relative to the workspace file area and must not contain '..'.");

        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (full != root && !full.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new ConnectorOperationException($"{Op}: path '{relative}' is outside the workspace file area.");

        // Walk existing components: a symlink anywhere on the path could point outside the root.
        var cursor = full;
        while (cursor.Length > root.Length)
        {
            var info = new FileInfo(cursor);
            if (info.Exists || Directory.Exists(cursor))
            {
                var fsi = Directory.Exists(cursor) ? (FileSystemInfo)new DirectoryInfo(cursor) : info;
                if (fsi.LinkTarget is not null)
                    throw new ConnectorOperationException($"{Op}: path '{relative}' goes through a symbolic link, which is not allowed.");
            }
            cursor = Path.GetDirectoryName(cursor) ?? root;
        }
        return full;
    }
}

public sealed class FileSystemReadHandler : FileSystemHandlerBase
{
    public FileSystemReadHandler(IConfiguration c, ConnectorCallContext ctx, ILogger<FileSystemReadHandler> l) : base(c, ctx, l) { }
    public override string OperationId => "read-file";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[FileSystem:read-file] ExecuteAsync entry");
        var root = WorkspaceRoot();
        var path = ConnectorInput.Required(input.RootElement, "path", Op);
        var full = Resolve(root, path);
        if (!File.Exists(full))
            throw new ConnectorOperationException($"{Op}: file '{path}' does not exist in the workspace file area.");
        if (new FileInfo(full).Length > MaxBytes)
            throw new ConnectorOperationException($"{Op}: file '{path}' is larger than 1 MB.");

        var content = await File.ReadAllTextAsync(full, Encoding.UTF8, ct);
        Logger.LogInformation("[FileSystem:read-file] ExecuteAsync exit");
        return ConnectorHttp.ToDocument(new { path, content, size_bytes = Encoding.UTF8.GetByteCount(content) });
    }
}

public sealed class FileSystemWriteHandler : FileSystemHandlerBase
{
    public FileSystemWriteHandler(IConfiguration c, ConnectorCallContext ctx, ILogger<FileSystemWriteHandler> l) : base(c, ctx, l) { }
    public override string OperationId => "write-file";

    public override async Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[FileSystem:write-file] ExecuteAsync entry");
        var root = WorkspaceRoot();
        var rootEl = input.RootElement;
        var path = ConnectorInput.Required(rootEl, "path", Op);
        var content = ConnectorInput.Optional(rootEl, "content") ?? string.Empty;
        var overwrite = ConnectorInput.Bool(rootEl, "overwrite");
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes)
            throw new ConnectorOperationException($"{Op}: content is larger than 1 MB.");

        var full = Resolve(root, path);
        if (Directory.Exists(full))
            throw new ConnectorOperationException($"{Op}: '{path}' is a folder.");
        if (File.Exists(full) && !overwrite)
            throw new ConnectorOperationException($"{Op}: '{path}' already exists. Set 'overwrite' to true to replace it.");

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), ct);
        Logger.LogInformation("[FileSystem:write-file] ExecuteAsync exit");
        return ConnectorHttp.ToDocument(new { path, size_bytes = Encoding.UTF8.GetByteCount(content), written = true });
    }
}

public sealed class FileSystemListHandler : FileSystemHandlerBase
{
    private const int MaxEntries = 500;
    public FileSystemListHandler(IConfiguration c, ConnectorCallContext ctx, ILogger<FileSystemListHandler> l) : base(c, ctx, l) { }
    public override string OperationId => "list-files";

    public override Task<JsonDocument> ExecuteAsync(JsonDocument input, ConnectorCredential? credential, CancellationToken ct)
    {
        Logger.LogInformation("[FileSystem:list-files] ExecuteAsync entry");
        var root = WorkspaceRoot();
        var path = ConnectorInput.Optional(input.RootElement, "path");
        var full = Resolve(root, path);
        if (!Directory.Exists(full))
            throw new ConnectorOperationException($"{Op}: folder '{path}' does not exist in the workspace file area.");

        var entries = new DirectoryInfo(full).EnumerateFileSystemInfos()
            .Where(e => e.LinkTarget is null)
            .Take(MaxEntries + 1)
            .Select(e => new
            {
                name = e.Name,
                path = Path.GetRelativePath(root, e.FullName).Replace('\\', '/'),
                is_folder = e is DirectoryInfo,
                size_bytes = e is FileInfo f ? f.Length : (long?)null,
                modified_utc = e.LastWriteTimeUtc.ToString("O"),
            }).ToList();

        var truncated = entries.Count > MaxEntries;
        Logger.LogInformation("[FileSystem:list-files] ExecuteAsync exit count={Count}", Math.Min(entries.Count, MaxEntries));
        return Task.FromResult(ConnectorHttp.ToDocument(new { entries = entries.Take(MaxEntries), truncated }));
    }
}
