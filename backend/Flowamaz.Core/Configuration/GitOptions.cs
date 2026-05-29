namespace Flowamaz.Core.Configuration;

/// <summary>Binds the "Git" section — location of the per-workspace bare workflow repositories.</summary>
public sealed class GitOptions
{
    public const string SectionName = "Git";

    /// <summary>
    /// Base directory under which each workspace gets <c>{workspaceId}/workflows.git</c>. Sourced
    /// from the GIT_REPOS_BASE_PATH env var; defaults to a "git-repos" folder beside the app binary.
    /// </summary>
    public string ReposBasePath { get; set; } = string.Empty;
}
