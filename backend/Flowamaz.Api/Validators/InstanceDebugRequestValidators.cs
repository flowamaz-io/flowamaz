using System.Text.Json;
using Flowamaz.Api.Controllers;
using FluentValidation;

namespace Flowamaz.Api.Validators;

/// <summary>
/// Validates a replay request. The optional payload override, if supplied, must be a parseable JSON
/// document so the replayed run starts from a well-formed trigger payload.
/// </summary>
public sealed class ReplayRequestValidator : AbstractValidator<ReplayRequest>
{
    public ReplayRequestValidator()
    {
        RuleFor(x => x.PayloadOverride)
            .Must(BeValidJson)
            .When(x => !string.IsNullOrWhiteSpace(x.PayloadOverride))
            .WithMessage("The payload override must be valid JSON. Fix the JSON, or omit it to reuse the original payload.");
    }

    private static bool BeValidJson(string? payload)
    {
        try
        {
            using var _ = JsonDocument.Parse(payload!);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Validates a request to set a Development breakpoint on a workflow node.</summary>
public sealed class SetBreakpointRequestValidator : AbstractValidator<SetBreakpointRequest>
{
    public SetBreakpointRequestValidator()
    {
        RuleFor(x => x.WorkflowId)
            .NotEmpty().WithMessage("A workflow must be specified to set a breakpoint.");

        RuleFor(x => x.NodeId)
            .NotEmpty().WithMessage("A node id must be specified to set a breakpoint.");
    }
}
