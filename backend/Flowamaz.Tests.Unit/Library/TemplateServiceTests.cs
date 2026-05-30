using System.Reflection;
using FluentAssertions;
using Flowamaz.Api.Authorization;
using Flowamaz.Api.Controllers;
using Flowamaz.Application.Library.Services;
using Flowamaz.Application.Workflow.DTOs;
using Flowamaz.Application.Workflow.Services;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Entities.Workflow;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Git;
using Flowamaz.Core.Interfaces.Git;
using Flowamaz.Core.Interfaces.Persistence;
using Flowamaz.Core.Interfaces.Repositories;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Library;

/// <summary>
/// TemplateService gallery operations + the official-template validity guarantee. Install reuses the
/// real WorkflowService create path (so the installed workflow is identical to a hand-authored one)
/// and atomically bumps install_count; publish refuses any workflow that is not Published. The final
/// test asserts every seeded official template's YAML passes the production workflow validator.
/// </summary>
public sealed class TemplateServiceTests
{
    private readonly Mock<IWorkflowTemplateRepository> _templates = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowInstanceRepository> _instances = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IWorkspaceGitService> _git = new();
    private readonly Mock<IEditionService> _edition = new();

    private readonly Guid _ws = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _org = Guid.NewGuid();

    private const string ValidYaml = """
        apiVersion: flowamaz/v1
        kind: Workflow
        metadata:
          id: sample
          name: "Sample"
        spec:
          trigger:
            type: webhook
          nodes:
            - id: start
              type: trigger
              label: "Start"
            - id: done
              type: end
              label: "Done"
          edges:
            - id: e1
              from: start
              to: done
        """;

    public TemplateServiceTests() =>
        _git.Setup(g => g.CommitWorkflowAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitResult("abc1234def5678", "main", null));

    private WorkflowService WorkflowSvc() => new(
        _definitions.Object, _versions.Object, _instances.Object, _unitOfWork.Object,
        new SfgParser(), _git.Object, _edition.Object, NullLogger<WorkflowService>.Instance);

    private TemplateService NewService() => new(
        _templates.Object, _definitions.Object, WorkflowSvc(),
        new Flowamaz.Application.Workflow.Validation.WorkflowValidator(
            NullLogger<Flowamaz.Application.Workflow.Validation.WorkflowValidator>.Instance),
        _unitOfWork.Object, NullLogger<TemplateService>.Instance);

    private WorkflowTemplate Template() => new()
    {
        Id = Guid.NewGuid(), Name = "Purchase Approval", Slug = "purchase-approval",
        Category = "Finance", Description = "desc", IsOfficial = true, YamlContent = ValidYaml,
    };

    [Fact]
    public async Task InstallTemplate_IncrementsInstallCount_Atomically()
    {
        var template = Template();
        _templates.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _definitions.Setup(r => r.SlugExistsInWorkspaceAsync(_ws, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await NewService().InstallTemplateAsync(template.Id, _ws, _user, "My Purchase Flow");

        // Atomicity lives in the repo's ExecuteUpdateAsync; the service must delegate to it exactly once.
        _templates.Verify(r => r.IncrementInstallCountAsync(template.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InstallTemplate_CreatesWorkflowDefinition_FromTemplateYaml()
    {
        var template = Template();
        _templates.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _definitions.Setup(r => r.SlugExistsInWorkspaceAsync(_ws, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        WorkflowDefinition? added = null;
        _definitions.Setup(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowDefinition, CancellationToken>((d, _) => added = d).Returns(Task.CompletedTask);

        var workflowId = await NewService().InstallTemplateAsync(template.Id, _ws, _user, "My Purchase Flow");

        added.Should().NotBeNull();
        added!.WorkspaceId.Should().Be(_ws);
        added.YamlContent.Should().Be(ValidYaml);
        added.Name.Should().Be("My Purchase Flow");
        workflowId.Should().Be(added.Id);
        _definitions.Verify(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishTemplate_RejectsWorkflow_NotPublished()
    {
        var workflowId = Guid.NewGuid();
        var draft = new WorkflowDefinition
        {
            Id = workflowId, WorkspaceId = _ws, Name = "Orders", Slug = "orders",
            YamlContent = ValidYaml, Status = WorkflowStatus.Draft,
        };
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(workflowId, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var details = new PublishTemplateDetails("My Template", "desc", "Finance", ["finance"], null);
        var act = async () => await NewService().PublishTemplateAsync(workflowId, _ws, _user, _org, details);

        (await act.Should().ThrowAsync<Flowamaz.Core.Exceptions.TemplateException>()
            .Where(e => e.HttpStatusCode == 422 && e.ErrorCode == "workflow_not_published"))
            .And.Message.Should().Contain("Publish the workflow");
        _templates.Verify(r => r.AddAsync(It.IsAny<WorkflowTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishTemplate_PublishedWorkflow_CreatesPendingTemplate()
    {
        var workflowId = Guid.NewGuid();
        var published = new WorkflowDefinition
        {
            Id = workflowId, WorkspaceId = _ws, Name = "Orders", Slug = "orders",
            YamlContent = ValidYaml, Status = WorkflowStatus.Published,
        };
        _definitions.Setup(r => r.GetByIdForWorkspaceAsync(workflowId, _ws, It.IsAny<CancellationToken>())).ReturnsAsync(published);
        _templates.Setup(r => r.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        WorkflowTemplate? created = null;
        _templates.Setup(r => r.AddAsync(It.IsAny<WorkflowTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowTemplate, CancellationToken>((t, _) => created = t).Returns(Task.CompletedTask);

        var details = new PublishTemplateDetails("My Template", "desc", "Finance", ["finance"], null);
        var result = await NewService().PublishTemplateAsync(workflowId, _ws, _user, _org, details);

        created.Should().NotBeNull();
        created!.ReviewStatus.Should().Be("pending");
        created.IsOfficial.Should().BeFalse();
        created.OrgId.Should().Be(_org);
        created.YamlContent.Should().Be(ValidYaml);
        result.ReviewStatus.Should().Be("pending");
    }

    [Fact]
    public async Task ListTemplates_RequiresNoAuth_AndReturnsItems()
    {
        // The service has no auth dependency: anyone (anonymous) can browse the gallery.
        _templates.Setup(r => r.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Template(), Template()]);

        var page = await NewService().ListTemplatesAsync(null, null, 1, 12);

        page.Total.Should().Be(2);
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public void ListAndGet_AreAnonymous_Install_RequiresDesignerRole()
    {
        // Authorization contract enforced by attributes on the controller (verified via reflection so
        // a refactor that drops [AllowAnonymous] or the Designer gate fails this test).
        var list = typeof(TemplatesController).GetMethod(nameof(TemplatesController.List))!;
        var get = typeof(TemplatesController).GetMethod(nameof(TemplatesController.Get))!;
        var install = typeof(TemplatesController).GetMethod(nameof(TemplatesController.Install))!;
        var publish = typeof(TemplatesController).GetMethod(nameof(TemplatesController.Publish))!;

        list.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        get.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();

        var installAuth = install.GetCustomAttribute<RequireWorkspaceRoleAttribute>();
        installAuth.Should().NotBeNull();
        installAuth!.Arguments.Should().Contain(WorkspaceRole.Designer);

        var publishAuth = publish.GetCustomAttribute<RequireWorkspaceRoleAttribute>();
        publishAuth.Should().NotBeNull();
        publishAuth!.Arguments.Should().Contain(WorkspaceRole.Designer);
    }

    [Fact]
    public async Task AllOfficialTemplates_PassValidator()
    {
        var validator = new Flowamaz.Application.Workflow.Validation.WorkflowValidator(
            NullLogger<Flowamaz.Application.Workflow.Validation.WorkflowValidator>.Instance);

        WorkflowTemplateSeeder.OfficialTemplates.Should().HaveCount(10);

        foreach (var template in WorkflowTemplateSeeder.OfficialTemplates)
        {
            var result = await validator.ValidateAsync(template.YamlContent);
            result.IsValid.Should().BeTrue(
                $"official template '{template.Slug}' must be valid SFG YAML but had errors: " +
                string.Join("; ", result.Errors.Select(e => $"{e.Code} {e.Message}")));
        }
    }
}
