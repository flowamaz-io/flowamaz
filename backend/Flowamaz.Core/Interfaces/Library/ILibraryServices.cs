using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Library;

/// <summary>A folder in a Library repository: its path and the files the sync reads from it.</summary>
public sealed record LibrarySourceItem(string Path, IReadOnlyDictionary<string, string> Files);

/// <summary>The Library repository as of one commit.</summary>
public sealed record LibrarySnapshot(string Repository, string CommitSha, IReadOnlyList<LibrarySourceItem> Items);

/// <summary>Reads the connectors or templates Library repository (GitHub in production).</summary>
public interface ILibrarySource
{
    /// <summary>Whether a repository is configured for <paramref name="source"/> ("connectors" or "templates").</summary>
    bool IsConfigured(string source);

    /// <summary>The repository's current commit, with every connector.yaml / template.yaml folder.</summary>
    Task<LibrarySnapshot> FetchAsync(string source, CancellationToken ct = default);
}

/// <summary>Library GitHub sync (FUNCTIONAL.md §23.7).</summary>
public interface ILibrarySyncService
{
    /// <summary>Syncs both repositories now. A failure leaves the Library as it was.</summary>
    Task<IReadOnlyList<LibrarySyncReport>> SyncAsync(CancellationToken ct = default);

    /// <summary>Where each repository stands (for staff).</summary>
    Task<IReadOnlyList<LibrarySyncStatus>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Asks for a sync soon (the push webhook): runs the background job now when one is scheduled.</summary>
    Task RequestSyncAsync(CancellationToken ct = default);
}

/// <summary>The Library as one workspace uses it: browse, install, keep connectors up to date, publish.</summary>
public interface ILibraryService
{
    Task<IReadOnlyList<LibraryConnectorItem>> ListConnectorsAsync(Guid workspaceId, string? query, string? category, CancellationToken ct = default);
    Task<LibraryConnectorDetail> GetConnectorAsync(Guid workspaceId, string connectorId, CancellationToken ct = default);
    Task<LibraryConnectorItem> InstallConnectorAsync(Guid workspaceId, string connectorId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<LibraryOutdatedItem>> GetOutdatedConnectorsAsync(Guid workspaceId, CancellationToken ct = default);
    Task<LibraryUpdateResult> UpgradeConnectorAsync(Guid workspaceId, string connectorId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<LibraryTemplateItem>> ListTemplatesAsync(Guid workspaceId, string? query, string? category, CancellationToken ct = default);
    Task<LibraryTemplateItem> GetTemplateAsync(Guid workspaceId, string slugOrId, CancellationToken ct = default);
    Task<Guid> InstallTemplateAsync(Guid workspaceId, string slugOrId, string? name, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<LibraryFormItem>> ListFormsAsync(Guid workspaceId, string? query, string? category, CancellationToken ct = default);
    Task<LibraryFormItem> GetFormAsync(Guid workspaceId, string slugOrId, CancellationToken ct = default);
    /// <summary>Saves a gate's fields as one of this workspace's forms, so other gates can reuse them.</summary>
    Task<LibraryFormItem> SaveFormAsync(Guid workspaceId, string name, string? description, string? category, IReadOnlyList<Flowamaz.Core.Workflow.GateFormField> fields, Guid userId, CancellationToken ct = default);
    Task DeleteFormAsync(Guid workspaceId, Guid formId, CancellationToken ct = default);

    Task<IReadOnlyList<LibraryPackItem>> ListPacksAsync(Guid workspaceId, string? query, CancellationToken ct = default);
    Task<LibraryPackItem> GetPackAsync(Guid workspaceId, string slugOrId, CancellationToken ct = default);
    /// <summary>Installs every connector and template the pack bundles; one item failing doesn't stop the rest.</summary>
    Task<LibraryPackInstallResult> InstallPackAsync(Guid workspaceId, string slugOrId, Guid userId, CancellationToken ct = default);

    Task<LibraryPublishResult> PublishConnectorAsync(Guid workspaceId, Guid orgId, string manifestYaml, CancellationToken ct = default);
    Task<LibraryPublishResult> PublishTemplateAsync(Guid workspaceId, Guid orgId, Guid userId, string manifestYaml, CancellationToken ct = default);
}
