using FluentAssertions;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// WorkflowService CRUD + publish (fix-02-02). YAML is validated through the real SfgParser before
/// any write (invalid → 422); slug collisions and active-instance deletes raise domain exceptions;
/// publish snapshots a production version and demotes the previous one. SLA threshold round-trips.
/// </summary>
public class WorkflowServiceTests
{
    private const string ValidYaml = """
        workflow: { id: w, version: v1, name: W }
        nodes:
          - { id: start, type: Trigger }
          - { id: a, type: Action }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: a }
          - { id: e2, from: a, to: done }
        """;

    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _id = Guid.NewGuid();

    private WorkflowService NewService() => new(
        _definitions.Object, _versions.Object, _instances.Object, _unitOfWork.Object,
        new SfgParser(), NullLogger<WorkflowService>.Instance);

    private WorkflowDefinition Definition() => new()
    {
        Id = _id, WorkspaceId = _ws, Name = "Orders", Slug = "orders", YamlContent = ValidYaml, Status = WorkflowStatus.Draft,
    };

    private CreateWorkflowDefinitionRequest CreateRequest(long? sla = null) =>
        new("Orders", "orders", ValidYaml, null, WorkflowCreatedByMethod.NaturalLanguage, sla);

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndReturnsResponseWithSla()
    {
        _definitions.Setup(r => r.SlugExistsInWorkspaceAsync(_ws, "orders", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        WorkflowDefinition? added = null;
        _definitions.Setup(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowDefinition, CancellationToken>((d, _) => added = d).Returns(Task.CompletedTask);

        var response = await NewService().CreateAsync(_ws, Guid.NewGuid(), CreateRequest(sla: 3_600_000));

        added.Should().NotBeNull();
        added!.SlaThresholdMs.Should().Be(3_600_000);
        response.SlaThresholdMs.Should().Be(3_600_000);
        response.Status.Should().Be(WorkflowStatus.Draft);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_Throws()
    {
        _definitions.Setup(r => r.SlugExistsInWorkspaceAsync(_ws, "orders", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = async () => await NewService().CreateAsync(_ws, Guid.NewGuid(), CreateRequest());

        await act.Should().ThrowAsync<WorkflowSlugExistsException>();
        _definitions.Verify(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_InvalidYaml_ThrowsParseExceptionBeforePersisting()
    {
        var bad = new CreateWorkflowDefinitionRequest("Orders", "orders", "not: [valid", null, WorkflowCreatedByMethod.NaturalLanguage);

        var act = async () => await NewService().CreateAsync(_ws, Guid.NewGuid(), bad);

        await act.Should().ThrowAsync<SfgParseException>();
        _definitions.Verify(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_FoundAndMissing()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(Definition());
        (await NewService().GetByIdAsync(_ws, _id)).Should().NotBeNull();

        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowDefinition?)null);
        (await NewService().GetByIdAsync(_ws, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesYamlAndSla()
    {
        var def = Definition();
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        var response = await NewService().UpdateAsync(_ws, _id, ValidYaml, slaThresholdMs: 90_000);

        response.Should().NotBeNull();
        def.SlaThresholdMs.Should().Be(90_000);
        _definitions.Verify(r => r.Update(def), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowDefinition?)null);
        (await NewService().UpdateAsync(_ws, Guid.NewGuid(), ValidYaml)).Should().BeNull();
    }

    [Fact]
    public async Task SoftDeleteAsync_ActiveInstances_Throws()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(Definition());
        _instances.Setup(r => r.HasActiveInstancesAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = async () => await NewService().SoftDeleteAsync(_ws, _id);

        await act.Should().ThrowAsync<WorkflowHasActiveInstancesException>();
    }

    [Fact]
    public async Task SoftDeleteAsync_HappyPath_MarksDeleted()
    {
        var def = Definition();
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);
        _instances.Setup(r => r.HasActiveInstancesAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        (await NewService().SoftDeleteAsync(_ws, _id)).Should().BeTrue();
        def.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteAsync_NotFound_ReturnsFalse()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowDefinition?)null);
        (await NewService().SoftDeleteAsync(_ws, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_MapsDefinitions()
    {
        _definitions.Setup(r => r.GetForWorkspaceAsync(_ws, It.IsAny<CancellationToken>())).ReturnsAsync([Definition()]);
        var list = await NewService().ListAsync(_ws);
        list.Should().ContainSingle().Which.Slug.Should().Be("orders");
    }

    [Fact]
    public async Task GetVersionsAsync_FoundAndMissing()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(Definition());
        _versions.Setup(r => r.GetForDefinitionAsync(_id, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowVersion { Id = Guid.NewGuid(), WorkspaceId = _ws, WorkflowDefinitionId = _id, CommitSha = "abc", BranchName = "main", Message = "m", IsProduction = true }]);
        (await NewService().GetVersionsAsync(_ws, _id))!.Should().ContainSingle();

        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowDefinition?)null);
        (await NewService().GetVersionsAsync(_ws, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_DemotesCurrentAndCreatesProductionVersion()
    {
        var def = Definition();
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);
        var current = new WorkflowVersion { Id = Guid.NewGuid(), WorkspaceId = _ws, WorkflowDefinitionId = _id, IsProduction = true };
        _versions.Setup(r => r.GetProductionAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(current);
        WorkflowVersion? added = null;
        _versions.Setup(r => r.AddAsync(It.IsAny<WorkflowVersion>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowVersion, CancellationToken>((v, _) => added = v).Returns(Task.CompletedTask);

        var response = await NewService().PublishAsync(_ws, _id, Guid.NewGuid());

        response.Should().NotBeNull();
        current.IsProduction.Should().BeFalse(); // previous demoted
        added!.IsProduction.Should().BeTrue();
        def.Status.Should().Be(WorkflowStatus.Published);
    }

    [Fact]
    public async Task PublishAsync_NotFound_ReturnsNull()
    {
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowDefinition?)null);
        (await NewService().PublishAsync(_ws, Guid.NewGuid(), Guid.NewGuid())).Should().BeNull();
    }
}
