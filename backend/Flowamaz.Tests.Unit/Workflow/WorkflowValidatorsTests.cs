using FluentAssertions;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Validators;
using Flowamaz.Core.Enums;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// Workflow request validators (fix-02-02): the SLA threshold, when supplied, must be greater than
/// zero; leaving it unset is valid (no SLA). Also covers the slug/name/decision rules.
/// </summary>
public class WorkflowValidatorsTests
{
    private readonly CreateWorkflowDefinitionRequestValidator _create = new();
    private readonly UpdateWorkflowDefinitionRequestValidator _update = new();
    private readonly GateDecisionRequestValidator _gate = new();

    private static CreateWorkflowDefinitionRequest NewCreate(long? sla) =>
        new("Orders", "orders", "nodes: []", null, WorkflowCreatedByMethod.NaturalLanguage, sla);

    [Theory]
    [InlineData(null, true)]
    [InlineData(60_000L, true)]
    [InlineData(0L, false)]
    [InlineData(-5L, false)]
    public void Create_validates_sla_threshold(long? sla, bool expectedValid)
    {
        _create.Validate(NewCreate(sla)).IsValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(90_000L, true)]
    [InlineData(0L, false)]
    public void Update_validates_sla_threshold(long? sla, bool expectedValid)
    {
        _update.Validate(new UpdateWorkflowDefinitionRequest("nodes: []", sla)).IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Create_rejects_invalid_slug()
    {
        _create.Validate(new CreateWorkflowDefinitionRequest("Orders", "Bad Slug!", "y", null, WorkflowCreatedByMethod.NaturalLanguage))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("approved", true)]
    [InlineData("rejected", true)]
    [InlineData("maybe", false)]
    public void Gate_decision_must_be_approved_or_rejected(string decision, bool expectedValid)
    {
        _gate.Validate(new GateDecisionRequest(decision, null)).IsValid.Should().Be(expectedValid);
    }
}
