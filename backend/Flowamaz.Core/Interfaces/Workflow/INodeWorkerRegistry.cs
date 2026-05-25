using Flowamaz.Core.Enums;

namespace Flowamaz.Core.Interfaces.Workflow;

/// <summary>Maps a <see cref="NodeType"/> to the <see cref="INodeWorker"/> that executes it.</summary>
public interface INodeWorkerRegistry
{
    /// <summary>The worker for <paramref name="type"/>, or null when no executor is registered.</summary>
    INodeWorker? Resolve(NodeType type);
}
