using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Entities.Workspaces;

/// <summary>
/// One of the three environments (Dev/Staging/Production) provisioned for every workspace.
/// Unique on WorkspaceId + <see cref="Name"/>. API keys are minted against a specific environment.
/// </summary>
public class WorkspaceEnvironment : BaseEntity
{
    public Guid WorkspaceId { get; set; }
    public WorkspaceEnvironmentType Name { get; set; }
}
