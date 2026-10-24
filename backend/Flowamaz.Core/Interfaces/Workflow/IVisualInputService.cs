using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface IVisualInputService
{
    /// <summary>
    /// One-shot: parse the image and return YAML. Low-confidence nodes are included (never dropped) with
    /// <c>config.review_needed: true</c> and are also listed in <see cref="VisualInputResult.LowConfidenceElements"/>.
    /// </summary>
    Task<VisualInputResult> ProcessImageAsync(byte[] imageBytes, string mimeType, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Review step: parse the image and return the full detected graph with per-node confidence and reason.</summary>
    Task<VisualPreviewResult> PreviewImageAsync(byte[] imageBytes, string mimeType, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Build workflow YAML (YamlDotNet) from a user-confirmed graph. No AI call.</summary>
    string BuildYamlFromConfirmedGraph(ConfirmedVisualGraph graph, string workflowId, string workflowName);
}
