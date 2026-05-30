using Flowamaz.Core.Entities;

namespace Flowamaz.Core.Entities.Notifications;

/// <summary>
/// A per-user key/value preference (JSON value). Used for things like the getting-started
/// checklist completion state, keyed "checklist_{workspaceId}". Unique per (user, key).
/// </summary>
public class UserPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON-serialised value.</summary>
    public string Value { get; set; } = "{}";
}
