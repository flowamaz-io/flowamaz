using FluentAssertions;
using FluentValidation.TestHelper;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Application.Workflow.Validators;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

/// <summary>Conversation → Workflow depth: thread parsing, the review step, warnings and the generation request.</summary>
public sealed class ConversationExtractionTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly ModelConfig TestModel = new("claude-sonnet-4-6", "anthropic", AiKeySource.Platform, "key", false, 200000, true, true);

    private const string RichExtraction = """
        Here is the process:
        ```json
        {
          "workflow_name": "Purchase approval",
          "summary": "Approves purchase requests by amount",
          "trigger": "A purchase request is submitted",
          "steps": [
            {"title": "Submit purchase request", "owner": "Anita", "system": null, "said_by": "Anita"},
            {"title": "Approve the request", "owner": "Rajan", "system": null, "said_by": "Rajan"},
            {"title": "Create the purchase order", "owner": "Finance", "system": "SAP", "said_by": "CFO"}
          ],
          "decisions": [{"condition": "amount above $5,000", "outcome": "CFO approves instead of Rajan", "said_by": "Rajan"}],
          "approvers": [{"who": "Rajan", "when": "up to $5,000"}, {"who": "CFO", "when": "above $5,000"}, {"who": "Legal", "when": null}],
          "agreements": ["Rajan approves up to $5,000"],
          "systems": ["SAP"],
          "open_questions": ["Who approves when the CFO is away?"],
          "confidence": 0.55
        }
        ```
        """;

    // ── Parsing ───────────────────────────────────────────────────────────────

    [Fact]
    public void Email_OutlookChainPastedNewestFirst_IsOrderedByDate_WithoutQuotesOrSignatures()
    {
        const string chain = """
            From: Rajan Kumar <rajan@acme.com>
            Sent: Tuesday, 4 November 2026 09:15
            To: Anita Rao <anita@acme.com>
            Subject: RE: Purchase approvals

            Agreed. I approve up to $5,000; anything above goes to the CFO.
            --
            Rajan Kumar | Operations
            > Can we agree how purchase approvals work?

            -----Original Message-----
            From: "Anita Rao" <anita@acme.com>
            Sent: Monday, 3 November 2026 17:40
            Subject: Purchase approvals

            Can we agree how purchase approvals work?
            Sent from my iPhone
            """;

        var messages = ConversationParser.Parse(chain, "email");

        messages.Select(m => m.Speaker).Should().Equal("Anita Rao", "Rajan Kumar");
        messages[0].Text.Should().Be("Can we agree how purchase approvals work?");
        messages[1].Text.Should().Be("Agreed. I approve up to $5,000; anything above goes to the CFO.");
    }

    [Fact]
    public void Email_GmailWroteLines_NameTheSpeaker()
    {
        const string chain = """
            Sounds good, I'll set it up in SAP.

            On Mon, 3 Nov 2026 at 10:02, Anita Rao <anita@acme.com> wrote:
            > quoted reply
            """;
        // A reply above its "On … wrote:" line has no header of its own; the named message below it is kept.
        const string thread = """
            On Mon, 3 Nov 2026 at 10:02, Anita Rao <anita@acme.com> wrote:
            Requests over $5,000 need the CFO.
            On Mon, 3 Nov 2026 at 11:30, cfo@acme.com wrote:
            Fine by me.
            """;

        ConversationParser.Parse(chain, "email").Should().BeEmpty();
        var messages = ConversationParser.Parse(thread, "email");
        messages.Select(m => m.Speaker).Should().Equal("Anita Rao", "cfo");
    }

    [Fact]
    public void Teams_VttTranscript_MergesConsecutiveCuesPerSpeaker()
    {
        const string vtt = """
            WEBVTT

            1
            00:00:01.000 --> 00:00:04.000
            <v Anita Rao>Requests come in by email.</v>

            2
            00:00:04.000 --> 00:00:06.000
            <v Anita Rao>Then Rajan approves.</v>

            3
            00:00:06.000 --> 00:00:09.000
            <v Rajan Kumar>Up to five thousand.</v>
            """;

        var messages = ConversationParser.Parse(vtt, "teams");

        messages.Should().HaveCount(2);
        messages[0].Should().Be(new ConversationParser.Message("Anita Rao", "Requests come in by email. Then Rajan approves."));
        messages[1].Speaker.Should().Be("Rajan Kumar");
    }

    [Fact]
    public void Teams_CopiedChat_UsesNameAndTimeHeaders()
    {
        const string chat = """
            Anita Rao 10:42 AM
            Who signs off on laptops?
            Rajan Kumar (10:45)
            I do, under $2,000.
            """;

        var messages = ConversationParser.Parse(chat, "teams");

        messages.Select(m => (m.Speaker, m.Text)).Should().Equal(
            ("Anita Rao", "Who signs off on laptops?"),
            ("Rajan Kumar", "I do, under $2,000."));
    }

    [Fact]
    public void Slack_HistoryResponseNewestFirst_UsesRealNamesAndTimestampOrder()
    {
        const string history = """
            {"ok": true, "messages": [
              {"user": "U2", "user_profile": {"real_name": "Rajan Kumar"}, "text": "Approved.", "ts": "1762160000.000200"},
              {"user": "U1", "user_profile": {"real_name": "Anita Rao"}, "text": "Please approve PR-12", "ts": "1762150000.000100"},
              {"user": "U3", "text": "", "ts": "1762140000.000100"}
            ]}
            """;

        var messages = ConversationParser.Parse(history, "slack");

        messages.Select(m => m.Speaker).Should().Equal("Anita Rao", "Rajan Kumar");
        ConversationParser.Participants(messages).Should().HaveCount(2);
    }

    [Fact]
    public void Generic_NameColonLines_FindSpeakers_ButNotLabelsOrNumberedSteps()
    {
        const string notes = """
            Anita: requests over $5k go to the CFO
            and the rest to Rajan
            Step 1: submit the form
            Note: this is a draft
            Rajan: agreed
            """;

        var messages = ConversationParser.Parse(notes, "generic");

        messages.Select(m => m.Speaker).Should().Equal("Anita", "Rajan");
        messages[0].Text.Should().Contain("and the rest to Rajan").And.Contain("Step 1: submit the form");
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_SendsWhoSaidWhat_AndReturnsTheFullExtraction_WithoutGenerating()
    {
        var (sut, ai, generator) = BuildSut(RichExtraction);
        const string thread = "Anita: Please approve my $8,000 request\nRajan: I can approve up to $5,000; CFO above that\nCFO: Finance raises the PO in SAP";

        var preview = await sut.ExtractAsync(thread, "generic", WorkspaceId);

        ai.Verify(x => x.CompleteAsync(TestModel, It.IsAny<string>(),
            It.Is<string>(m => m.StartsWith("Anita: Please approve") && m.Contains("\nRajan: ")), It.IsAny<CancellationToken>()), Times.Once);
        generator.VerifyNoOtherCalls();

        var e = preview.Extraction;
        e.Participants.Select(p => p.Name).Should().BeEquivalentTo("Anita", "Rajan", "CFO");
        e.Steps.Should().HaveCount(3);
        e.Steps[2].Should().Be(new ConversationStep("Create the purchase order", "Finance", "SAP", "CFO"));
        e.Decisions.Should().ContainSingle(d => d.Condition == "amount above $5,000");
        e.Approvers.Select(a => a.Who).Should().Equal("Rajan", "CFO", "Legal");
        e.Agreements.Should().Equal("Rajan approves up to $5,000");
        e.OpenQuestions.Should().ContainSingle();
        e.Confidence.Should().Be(0.55);
        preview.TokensUsed.Should().Be(500);
    }

    [Fact]
    public async Task ExtractAsync_Warns_AboutLowConfidence_UnmentionedPeople_AndOpenQuestions()
    {
        var (sut, _, _) = BuildSut(RichExtraction);

        var preview = await sut.ExtractAsync("Anita: approve please\nRajan: up to $5,000, CFO above", "generic", WorkspaceId);

        preview.Warnings.Should().Contain(w => w.Contains("55% confident"));
        preview.Warnings.Should().Contain(w => w.Contains("\"Legal\" is listed as an approver"));
        preview.Warnings.Should().Contain(w => w.Contains("\"Finance\" is given as a step owner"));
        preview.Warnings.Should().Contain("Left undecided in the thread: Who approves when the CFO is away?");
        preview.Warnings.Should().NotContain(w => w.Contains("\"Rajan\"") || w.Contains("\"CFO\""));
    }

    [Fact]
    public async Task ExtractAsync_AnswerThatIsNotJson_FailsWithAnActionableError()
    {
        var (sut, _, generator) = BuildSut("Sorry, I can't help with that.");

        var act = () => sut.ExtractAsync("Anita: hi\nRajan: hello", "generic", WorkspaceId);

        (await act.Should().ThrowAsync<ConversationExtractionException>()).Which.Message.Should().Contain("try Extract again");
        generator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExtractAsync_NoSteps_FailsInsteadOfGeneratingAnEmptyWorkflow()
    {
        var (sut, _, _) = BuildSut("""{"workflow_name": "Chat", "steps": [], "confidence": 0.9}""");

        var act = () => sut.ExtractAsync("Anita: lunch?\nRajan: sure", "generic", WorkspaceId);

        (await act.Should().ThrowAsync<ConversationExtractionException>()).Which.ErrorCode.Should().Be("CONVERSATION_EXTRACTION_FAILED");
    }

    [Theory]
    [InlineData("""{"is_process": false, "workflow_name": "Lunch", "steps": [], "confidence": 0.9}""")]
    // CC test, live model: a lunch chat came back as two steps at 10% confidence, flagged as not a process.
    [InlineData("""{"workflow_name": "Lunch plan", "steps": [{"title": "Propose lunch time"}, {"title": "Confirm the place"}], "confidence": 0.1}""")]
    [InlineData("""{"is_process": false, "workflow_name": "Lunch plan", "steps": [{"title": "Propose lunch time"}], "confidence": 0.6}""")]
    public void ParseExtraction_SmallTalk_IsRejectedAsNotAProcess(string answer)
    {
        var act = () => ConversationImportService.ParseExtraction(answer, []);

        act.Should().Throw<ConversationExtractionException>().Which.Message.Should().StartWith("This conversation doesn't describe a process");
    }

    [Fact]
    public void ParseExtraction_AcceptsTheEarlierFlatShape()
    {
        var e = ConversationImportService.ParseExtraction("""
            {"workflow_name": "PO", "steps": ["Submit", "Approve"], "approvers": ["Rajan"], "rules": "CFO above $5,000"}
            """, []);

        e.Steps.Select(s => s.Title).Should().Equal("Submit", "Approve");
        e.Approvers.Should().Equal(new ConversationApprover("Rajan"));
        e.Agreements.Should().Equal("CFO above $5,000");
        e.Confidence.Should().Be(0.5, "a missing confidence is not reported as high");
    }

    // ── Generation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_UsesTheReviewedExtraction_WithOwnersSystemsConditionsAndAgreements()
    {
        var (sut, ai, generator) = BuildSut(RichExtraction);
        var reviewed = ConversationImportService.ParseExtraction(RichExtraction, [new ConversationParticipant("Anita", 2)]) with
        {
            Steps = [new ConversationStep("Submit purchase request", "Anita"), new ConversationStep("Raise PO", "Finance", "SAP")],
        };
        NlWorkflowRequest? sent = null;
        generator.Setup(x => x.GenerateAsync(It.IsAny<NlWorkflowRequest>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .Callback<NlWorkflowRequest, Guid, CancellationToken>((r, _, _) => sent = r)
            .ReturnsAsync(new GenerationResult("yaml", new ValidationResult(true, [], [], []), 400, false));

        var result = await sut.GenerateAsync(reviewed, WorkspaceId);

        ai.VerifyNoOtherCalls(); // the thread is not read again
        sent!.StepsDescription.Should().Be("1. Submit purchase request (done by Anita)\n2. Raise PO (done by Finance, in SAP)");
        sent.RulesAndConstraints.Should().Contain("Approvers: Rajan (up to $5,000); CFO (above $5,000); Legal.")
            .And.Contain("If amount above $5,000, then CFO approves instead of Rajan.")
            .And.Contain("Agreed: Rajan approves up to $5,000");
        sent.SystemsAndAi.Should().Be("SAP");
        sent.ExistingContext.Should().Contain("People in the thread: Anita.");
        sent.ExistingContext.Should().Contain("follow them: one node per step, in this order").And.Contain("Do not add other steps");
        result.ExtractedProcess.Steps.Should().Equal("Submit purchase request", "Raise PO");
        result.TokensUsed.Should().Be(400);
    }

    [Fact]
    public async Task ImportAsync_ExtractsThenGenerates_AndCountsBothCalls()
    {
        var (sut, _, generator) = BuildSut(RichExtraction);
        generator.Setup(x => x.GenerateAsync(It.IsAny<NlWorkflowRequest>(), WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationResult("yaml", new ValidationResult(true, [], [], []), 400, false));

        var result = await sut.ImportAsync("Anita: approve\nRajan: ok", "generic", WorkspaceId);

        result.TokensUsed.Should().Be(900);
        result.YamlContent.Should().Be("yaml");
    }

    // ── Validation of the reviewed extraction ────────────────────────────────

    [Fact]
    public void Validator_RejectsNoSteps_EmptyStepText_AndConditionsWithoutOutcome()
    {
        var validator = new ConversationExtractionValidator();
        var ok = ConversationImportService.ParseExtraction(RichExtraction, []);

        validator.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(ok with { Steps = [] }).ShouldHaveValidationErrorFor(x => x.Steps)
            .WithErrorMessage("Every step was removed. Keep or add at least one step, or describe the process in plain English instead.");
        validator.TestValidate(ok with { Steps = [new ConversationStep(" ")] }).IsValid.Should().BeFalse();
        validator.TestValidate(ok with { Decisions = [new ConversationDecision("amount above $5,000", "")] }).IsValid.Should().BeFalse();
        validator.TestValidate(ok with { Steps = [new ConversationStep("Approve")] }).ShouldHaveValidationErrorFor(x => x.Steps)
            .WithErrorMessage("A workflow needs at least two steps. Add the step before or after this one (who does what next).");
        validator.TestValidate(ok with { Steps = Enumerable.Range(0, 24).Select(i => new ConversationStep($"Step {i}")).ToList() })
            .ShouldHaveValidationErrorFor(x => x.Steps);
    }

    private static (ConversationImportService, Mock<IAiCompletionService>, Mock<INlYamlGenerationService>) BuildSut(string aiAnswer)
    {
        var ai = new Mock<IAiCompletionService>();
        ai.Setup(x => x.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiCompletionResult(aiAnswer, 300, 200));
        var models = new Mock<IModelResolutionService>();
        models.Setup(x => x.ResolveModelConfigAsync(AiFunctionIds.NlYaml, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(TestModel);
        var generator = new Mock<INlYamlGenerationService>();
        var sut = new ConversationImportService(ai.Object, models.Object, new Mock<IAiTokenMeteringService>().Object, generator.Object,
            NullLogger<ConversationImportService>.Instance);
        return (sut, ai, generator);
    }
}
