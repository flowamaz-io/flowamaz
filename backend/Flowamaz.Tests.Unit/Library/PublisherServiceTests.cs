using FluentAssertions;
using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Entities.Library;
using Flowamaz.Core.Entities.Platform;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Services.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Library;

/// <summary>Library publishers (FUNCTIONAL.md §23.9): profiles, portal submissions, staff reviews.</summary>
public sealed class PublisherServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly Mock<IConnectorSubmissionPrService> _prs = new();
    private readonly FlowAmazDbContext _db;
    private readonly Organisation _acme = new() { Id = Guid.NewGuid(), Name = "Acme Ltd", Slug = "acme" };
    private readonly Guid _owner = Guid.NewGuid();

    public PublisherServiceTests()
    {
        _db = new FlowAmazDbContext(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase("publishers-" + Guid.NewGuid()).Options);
        _db.Organisations.Add(_acme);
        _db.OrgUsers.Add(new OrgUser { Id = _owner, OrgId = _acme.Id, Email = "o@acme.test", Name = "Olivia", IsOrgOwner = true });
        _db.SaveChanges();
    }

    private PublisherService Service() => new(_db, _prs.Object, NullLogger<PublisherService>.Instance, _clock);

    private ConnectorSubmission Submission(string status = "pending", DateTime? checkedAt = null) => new()
    {
        OrgId = _acme.Id, ConnectorName = "Invoice Sync", ManifestYaml = "id: invoice-sync", Status = status,
        GithubPrUrl = "https://github.com/flowamaz-io/connectors/pull/7", PublisherId = "acme", ConnectorId = "invoice-sync",
        PrCheckedAt = checkedAt, CreatedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1),
    };

    [Fact]
    public async Task Profile_lists_only_live_items_with_totals()
    {
        _db.ConnectorDefinitions.Add(new ConnectorDefinition
        {
            ConnectorId = "invoice-sync", PublisherId = "acme", DisplayName = "Invoice Sync", Version = "1.2.0", Category = "finance",
            InstallCount = 12, AverageRating = 4.5m, RatingCount = 4, ManifestJson = "{}",
        });
        _db.ConnectorDefinitions.Add(new ConnectorDefinition
        {
            ConnectorId = "private-one", PublisherId = "acme", DisplayName = "Private", Version = "1.0.0", WorkspaceId = Guid.NewGuid(), ManifestJson = "{}",
        });
        _db.WorkflowTemplates.Add(new WorkflowTemplate { Name = "Approved PO", Slug = "approved-po", OrgId = _acme.Id, ReviewStatus = "approved", InstallCount = 3 });
        _db.WorkflowTemplates.Add(new WorkflowTemplate { Name = "Pending PO", Slug = "pending-po", OrgId = _acme.Id, ReviewStatus = "pending" });
        _db.FormDefinitions.Add(new FormDefinition { Slug = "acme-ws-form", Name = "Workspace form", WorkspaceId = Guid.NewGuid(), PublisherId = "acme" });
        await _db.SaveChangesAsync();

        var profile = await Service().GetProfileAsync("ACME");

        profile.DisplayName.Should().Be("Acme Ltd", "with no profile row, the organisation's name is used");
        profile.Items.Select(i => i.Id).Should().Equal("invoice-sync", "approved-po");
        profile.Totals.Should().Be(new PublisherTotals(2, 15, 4.5m, 4));
        profile.IsVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Official_profile_is_verified_and_unknown_publishers_404()
    {
        _db.PublisherProfiles.Add(new PublisherProfile { PublisherId = "flowamaz-io", DisplayName = "Flowamaz", IsVerified = true });
        await _db.SaveChangesAsync();

        (await Service().GetProfileAsync("flowamaz-io")).Should().Match<PublisherProfileView>(p => p.IsOfficial && p.IsVerified);
        var missing = () => Service().GetProfileAsync("nobody-here");
        (await missing.Should().ThrowAsync<LibraryException>()).Which.HttpStatusCode.Should().Be(404);
        var odd = () => Service().GetProfileAsync("../etc");
        await odd.Should().ThrowAsync<LibraryException>();
    }

    [Theory]
    [InlineData("merged", "merged", null)]
    [InlineData("closed", "rejected", "closed without merging")]
    [InlineData("open", "pending", null)]
    public async Task Portal_follows_the_pull_request(string prState, string status, string? note)
    {
        _db.ConnectorSubmissions.Add(Submission());
        await _db.SaveChangesAsync();
        _prs.Setup(p => p.GetPullRequestStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(prState);

        var portal = await Service().GetPortalAsync(_acme.Id);

        var s = portal.Submissions.Single();
        s.Status.Should().Be(status);
        s.CheckedAt.Should().NotBeNull();
        if (note is null) s.ReviewerNotes.Should().BeNull();
        else s.ReviewerNotes.Should().Contain(note);
        portal.PublisherId.Should().Be("acme");
    }

    [Fact]
    public async Task Portal_marks_a_submission_live_once_the_sync_has_it_without_asking_github()
    {
        _db.ConnectorSubmissions.Add(Submission("merged"));
        _db.ConnectorDefinitions.Add(new ConnectorDefinition { ConnectorId = "invoice-sync", PublisherId = "acme", DisplayName = "Invoice Sync", ManifestJson = "{}" });
        await _db.SaveChangesAsync();

        var s = (await Service().GetPortalAsync(_acme.Id)).Submissions.Single();

        s.Status.Should().Be("live");
        s.StatusText.Should().Be("Live in the Library.");
        _prs.Verify(p => p.GetPullRequestStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Portal_keeps_the_last_status_when_github_cannot_be_asked_and_does_not_recheck_too_often()
    {
        _db.ConnectorSubmissions.Add(Submission(checkedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-3)));
        _db.ConnectorSubmissions.Add(Submission());
        await _db.SaveChangesAsync();
        _prs.Setup(p => p.GetPullRequestStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var portal = await Service().GetPortalAsync(_acme.Id);

        portal.Submissions.Should().OnlyContain(s => s.Status == "pending");
        _prs.Verify(p => p.GetPullRequestStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "the one checked 3 minutes ago waits; the other is asked once");
    }

    [Theory]
    [InlineData("", null, null, null, "display name")]
    [InlineData("Acme", null, "http://acme.example", null, "https://")]
    [InlineData("Acme", null, "acme.example", null, "https://")]
    [InlineData("Acme", null, null, "acme_labs", "GitHub account")]
    [InlineData("Acme", null, null, "-acme", "GitHub account")]
    public void CheckProfile_explains_each_problem(string name, string? bio, string? site, string? github, string expected)
    {
        var act = () => PublisherService.CheckProfile(new UpdatePublisherProfileRequest(name, bio, site, github));
        act.Should().Throw<LibraryException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void CheckProfile_accepts_a_github_url_or_at_name()
    {
        PublisherService.CheckProfile(new("Acme", " Makes things ", "https://acme.example", "https://github.com/acme-labs/")).Should()
            .Be(("Acme", "Makes things", "https://acme.example", "acme-labs"));
        PublisherService.CheckProfile(new("Acme", null, " ", "@acme")).Github.Should().Be("acme");
    }

    [Fact]
    public async Task UpdateProfile_creates_then_updates_the_orgs_profile()
    {
        await Service().UpdateProfileAsync(_acme.Id, new("Acme Labs", "Finance connectors.", "https://acme.example", "acme"), _owner);
        var view = await Service().UpdateProfileAsync(_acme.Id, new("Acme Labs", "Finance and HR connectors.", null, null), _owner);

        view.DisplayName.Should().Be("Acme Labs");
        view.Bio.Should().Be("Finance and HR connectors.");
        var row = await _db.PublisherProfiles.SingleAsync(p => p.PublisherId == "acme");
        row.OrgId.Should().Be(_acme.Id);
        row.WebsiteUrl.Should().BeNull();
        row.IsVerified.Should().BeFalse("only staff verify publishers");
    }

    [Fact]
    public async Task Approving_a_template_makes_it_live_notifies_the_owners_and_leaves_PublisherId_alone()
    {
        var template = new WorkflowTemplate { Name = "Acme PO", Slug = "acme-po", OrgId = _acme.Id, ReviewStatus = "pending", YamlContent = "x" };
        _db.WorkflowTemplates.Add(template);
        await _db.SaveChangesAsync();

        (await Service().GetReviewQueueAsync()).Templates.Should().ContainSingle(t => t.Id == template.Id && t.PublisherId == "acme" && t.OrgName == "Acme Ltd");
        await Service().ApproveTemplateAsync(template.Id, "Nice work.", Guid.NewGuid());

        var row = await _db.WorkflowTemplates.SingleAsync(t => t.Id == template.Id);
        row.ReviewStatus.Should().Be("approved");
        row.ReviewNotes.Should().Be("Nice work.");
        row.PublisherId.Should().BeNull("a PublisherId would let the Library sync deprecate it as removed from the repository");
        var note = await _db.Notifications.SingleAsync();
        note.UserId.Should().Be(_owner);
        note.Type.Should().Be("library_template_approved");
        (await Service().GetProfileAsync("acme")).Items.Should().ContainSingle(i => i.Id == "acme-po");
        var again = () => Service().ApproveTemplateAsync(template.Id, null, Guid.NewGuid());
        (await again.Should().ThrowAsync<LibraryException>()).Which.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Rejecting_needs_a_note_the_publisher_can_act_on()
    {
        var template = new WorkflowTemplate { Name = "Acme PO", Slug = "acme-po", OrgId = _acme.Id, ReviewStatus = "pending", YamlContent = "x" };
        _db.WorkflowTemplates.Add(template);
        await _db.SaveChangesAsync();

        var noNote = () => Service().RejectTemplateAsync(template.Id, "no", Guid.NewGuid());
        await noNote.Should().ThrowAsync<LibraryException>().WithMessage("*at least 10 characters*");
        await Service().RejectTemplateAsync(template.Id, "The approval step has no assignee.", Guid.NewGuid());

        var s = (await Service().GetPortalAsync(_acme.Id)).Submissions.Single();
        s.Status.Should().Be("rejected");
        s.ReviewerNotes.Should().Be("The approval step has no assignee.");
        s.StatusText.Should().Contain("publish it again");
        (await _db.Notifications.SingleAsync()).Message.Should().Contain("The approval step has no assignee.");
    }
}
