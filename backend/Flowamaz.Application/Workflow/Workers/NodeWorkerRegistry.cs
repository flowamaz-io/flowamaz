using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Workflow;

namespace Flowamaz.Application.Workflow.Workers;

/// <summary>
/// Indexes all registered <see cref="INodeWorker"/> implementations by their <see cref="NodeType"/>.
/// The worker loop resolves the right executor for the node it is about to run.
/// </summary>
public sealed class NodeWorkerRegistry : INodeWorkerRegistry
{
    private readonly IReadOnlyDictionary<NodeType, INodeWorker> _workers;

    public NodeWorkerRegistry(IEnumerable<INodeWorker> workers)
    {
        var map = new Dictionary<NodeType, INodeWorker>();
        foreach (var worker in workers) map[worker.SupportedType] = worker; // last registration wins
        _workers = map;
    }

    public INodeWorker? Resolve(NodeType type) => _workers.GetValueOrDefault(type);
}
