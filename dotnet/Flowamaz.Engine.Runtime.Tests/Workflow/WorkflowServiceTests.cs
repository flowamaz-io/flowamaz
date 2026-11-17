using FluentAssertions;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Git;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Runtime;
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
    private readonly Mock<IWorkflowVersionStore> _store = new();
    private readonly Mock<IWorkflowAdmission> _admission = new();
    private readonly Mock<IWorkflowEventSink> _events = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _id = Guid.NewGuid();

    public WorkflowServiceTests() =>
        _store.Setup(s => s.SaveAsync(It.IsAny<WorkflowSave>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowSaveResult("abc1234def5678", "main"));

    private WorkflowService NewService() => new(
        _definitions.Object, _versions.Object, _instances.Object, _unitOfWork.Object,
        new SfgParser(), _store.Object, _admission.Object, _events.Object, NullLogger<WorkflowService>.Instance);

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
    public async Task CreateAsync_InvalidYaml_SavesAsDraft_ParserRunsOnPublishOnly()
    {
        // Structurally invalid YAML is accepted at create time — parser only runs at PublishAsync.
        var bad = new CreateWorkflowDefinitionRequest("Orders", "orders", "not: [valid", null, WorkflowCreatedByMethod.NaturalLanguage);
        _definitions.Setup(r => r.SlugExistsInWorkspaceAsync(_ws, "orders", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = async () => await NewService().CreateAsync(_ws, Guid.NewGuid(), bad);

        await act.Should().NotThrowAsync();
        _definitions.Verify(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmptyYaml_NonCanvas_ThrowsInvalidOperationException()
    {
        var empty = new CreateWorkflowDefinitionRequest("Orders", "orders", "", null, WorkflowCreatedByMethod.NaturalLanguage);

        var act = async () => await NewService().CreateAsync(_ws, Guid.NewGuid(), empty);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*YamlContent cannot be empty*");
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
    public async Task UpdateAsync_CreditsTheSaverAndCoEditorsOnTheCommit()
    {
        var def = Definition();
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await NewService().UpdateAsync(_ws, _id, ValidYaml, null, default,
            new CommitAuthor("Ana Lee", "ana@acme.test"),
            [new CommitAuthor("Ben\nEvil", "ben@acme.test"), new CommitAuthor("Ana again", "ANA@acme.test")]);

        _store.Verify(s => s.SaveAsync(It.Is<WorkflowSave>(save =>
            save.WorkspaceId == _ws && save.WorkflowId == _id && save.YamlContent == ValidYaml
            && save.Message.Contains("\n\nCo-authored-by: BenEvil <ben@acme.test>") && !save.Message.Contains("Ana again")
            && save.Author == new CommitAuthor("Ana Lee", "ana@acme.test")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CommitMessage_WithoutCoAuthors_IsJustTheSubject()
    {
        WorkflowService.CommitMessage("Update Expenses", null, null).Should().Be("Update Expenses");
        WorkflowService.CommitMessage("Update Expenses", new CommitAuthor("A", "a@x.test"), [new CommitAuthor("No email", "")])
            .Should().Be("Update Expenses");
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
    public async Task DeleteAsync_Draft_SoftDeletes()
    {
        var def = Definition(); // Draft
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);
        var actor = Guid.NewGuid();

        await NewService().DeleteAsync(_id, _ws, actor);

        def.IsDeleted.Should().BeTrue();
        def.DeletedAt.Should().NotBeNull();
        def.UpdatedBy.Should().Be(actor);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Published_ThrowsNotDraft()
    {
        var def = Definition();
        def.Status = WorkflowStatus.Published;
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        var act = async () => await NewService().DeleteAsync(_id, _ws, Guid.NewGuid());

        await act.Should().ThrowAsync<WorkflowNotDraftException>();
        def.IsDeleted.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WrongWorkspace_ThrowsNotFound()
    {
        // Repository scopes by workspace — a wrong-workspace id resolves to null.
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(It.IsAny<Guid>(), _ws, It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowDefinition?)null);

        var act = async () => await NewService().DeleteAsync(Guid.NewGuid(), _ws, Guid.NewGuid());

        await act.Should().ThrowAsync<WorkflowNotFoundException>();
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

    private const string ScheduledYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata: { id: nightly, name: Nightly }
        spec:
          trigger:
            type: schedule
            config: { cron: "CRON", timezone: "Asia/Kuala_Lumpur" }
          nodes:
            - { id: start, type: trigger }
            - { id: done, type: end }
          edges:
            - { id: e1, from: start, to: done }
        """;

    [Fact]
    public async Task PublishAsync_ScheduleTrigger_RecordsTriggerType()
    {
        var def = Definition();
        def.YamlContent = ScheduledYaml.Replace("CRON", "0 9 * * 1-5");
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        await NewService().PublishAsync(_ws, _id, Guid.NewGuid());

        def.TriggerType.Should().Be(WorkflowTriggerType.Schedule);
    }

    [Fact]
    public async Task PublishAsync_InvalidSchedule_IsRejected_WithActionableMessage()
    {
        var def = Definition();
        def.YamlContent = ScheduledYaml.Replace("CRON", "every morning");
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(_id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(def);

        var act = () => NewService().PublishAsync(_ws, _id, Guid.NewGuid());

        (await act.Should().ThrowAsync<Flowamaz.Core.Exceptions.InvalidScheduleException>())
            .Which.Message.Should().Contain("0 9 * * 1-5");
        def.Status.Should().Be(WorkflowStatus.Draft);
    }

    // ── Archive / unarchive ────────────────────────────────────────────────────

    private WorkflowDefinition Existing(WorkflowStatus status)
    {
        var definition = new WorkflowDefinition { Id = Guid.NewGuid(), WorkspaceId = _ws, Name = "Loan approval", Slug = "loan", Status = status, YamlContent = "x" };
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(definition.Id, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        return definition;
    }

    [Fact]
    public async Task ArchiveAsync_archives_a_published_workflow()
    {
        var definition = Existing(WorkflowStatus.Published);

        var response = await NewService().ArchiveAsync(_ws, definition.Id, Guid.NewGuid());

        response.Status.Should().Be(WorkflowStatus.Archived);
        _definitions.Verify(r => r.Update(It.Is<WorkflowDefinition>(d => d.Status == WorkflowStatus.Archived)), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveAsync_refuses_a_draft_which_is_deleted_instead()
    {
        var definition = Existing(WorkflowStatus.Draft);

        var act = () => NewService().ArchiveAsync(_ws, definition.Id, Guid.NewGuid());

        (await act.Should().ThrowAsync<WorkflowArchiveException>()).Which.HttpStatusCode.Should().Be(422);
    }

    [Fact]
    public async Task ArchiveAsync_of_an_unknown_workflow_is_404()
    {
        var act = () => NewService().ArchiveAsync(_ws, Guid.NewGuid(), Guid.NewGuid());
        await act.Should().ThrowAsync<WorkflowNotFoundException>();
    }

    [Fact]
    public async Task UnarchiveAsync_checks_the_workflow_limit_then_publishes_again()
    {
        var definition = Existing(WorkflowStatus.Archived);

        var response = await NewService().UnarchiveAsync(_ws, definition.Id, Guid.NewGuid());

        response.Status.Should().Be(WorkflowStatus.Published);
        _admission.Verify(a => a.EnsureCanAddAsync(_ws, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.WorkflowChangedAsync(
            It.Is<WorkflowChangedEvent>(ev => ev.Action == "unarchived" && ev.WorkflowId == definition.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnarchiveAsync_at_the_limit_leaves_it_archived()
    {
        var definition = Existing(WorkflowStatus.Archived);
        _admission.Setup(a => a.EnsureCanAddAsync(_ws, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Workflow limit reached."));

        var act = () => NewService().UnarchiveAsync(_ws, definition.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
        definition.Status.Should().Be(WorkflowStatus.Archived);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_of_an_archived_workflow_says_to_unarchive_first()
    {
        var definition = Existing(WorkflowStatus.Archived);

        var act = () => NewService().PublishAsync(_ws, definition.Id, Guid.NewGuid());

        (await act.Should().ThrowAsync<WorkflowArchiveException>()).Which.Message.Should().Contain("Unarchive it first");
    }

    [Fact]
    public async Task PublishAsync_versions_and_tags_the_release_and_reports_it()
    {
        var definition = Existing(WorkflowStatus.Draft);
        definition.YamlContent = ValidYaml;
        _versions.Setup(v => v.GetForDefinitionAsync(definition.Id, _ws, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkflowVersion { WorkspaceId = _ws, WorkflowDefinitionId = definition.Id, CommitSha = "old", YamlContent = ValidYaml }]);
        _store.Setup(s => s.TagReleaseAsync(_ws, definition.Id, 2, It.IsAny<CancellationToken>())).ReturnsAsync("tag/v2");

        var version = await NewService().PublishAsync(_ws, definition.Id, Guid.NewGuid());

        version!.CommitSha.Should().Be("abc1234def5678");
        version.TagName.Should().Be("tag/v2");
        _events.Verify(e => e.WorkflowChangedAsync(It.Is<WorkflowChangedEvent>(ev =>
            ev.Action == "published" && ev.VersionNumber == 2 && ev.VersionRef == "abc1234def5678"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_refused_by_the_host_saves_nothing()
    {
        _admission.Setup(a => a.EnsureCanAddAsync(_ws, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Workflow limit reached."));

        var act = () => NewService().CreateAsync(_ws, Guid.NewGuid(), CreateRequest());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _definitions.Verify(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Default_store_gives_each_save_its_own_reference_and_tags_releases()
    {
        var store = DatabaseWorkflowVersionStore.Instance;
        var save = new WorkflowSave(_ws, _id, ValidYaml, "Update", null);

        var first = await store.SaveAsync(save, default);
        var second = await store.SaveAsync(save, default);

        first!.VersionRef.Should().NotBe(second!.VersionRef);
        first.VersionRef.Length.Should().BeLessThanOrEqualTo(64);
        (await store.TagReleaseAsync(_ws, _id, 3, default)).Should().Be("v3");
    }
}
