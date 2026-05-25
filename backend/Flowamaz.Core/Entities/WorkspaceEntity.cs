namespace Flowamaz.Core.Entities;

public abstract class WorkspaceEntity : AuditableEntity
{
    public Guid WorkspaceId { get; set; }
}
