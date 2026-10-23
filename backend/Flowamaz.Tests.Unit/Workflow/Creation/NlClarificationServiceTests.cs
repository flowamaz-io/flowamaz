using FluentAssertions;
using Flowamaz.Application.Workflow.Creation;
using Flowamaz.Application.Workflow.Validators;
using Flowamaz.Core.Constants;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow.Creation;

public sealed class NlClarificationServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly ModelConfig TestModel = new("model-from-resolution", "anthropic",
        AiKeySource.Platform, "key", false, 200000, true, true);

    private static readonly NlWorkflowRequest Complete = new(
        WorkflowName: "Purchase Approval",
        Purpose: "Turn purchase requests into purchase orders",
        TriggerDescription: "An employee submits the purchase request form",
        StepsDescription: "1. Validate the requester is active.\n2. The line manager approves requests over RM5000.\n3. If rejected, notify the requester with the reason.\n4. Create the PO in SAP.",
        RulesAndConstraints: "Manager responds within 48 hours",
        SystemsAndAi: "SAP for purchase orders, Slack for notifications");

    private static List<string> Topics(NlWorkflowRequest r) =>
        NlClarificationService.DetectMissingEssentials(r).Select(q => q.Topic).ToList();

    // ─── Heuristics ──────────────────────────────────────────────────────────

    [Fact]
    public void Heuristics_CompleteDescription_AsksNothing()
    {
        Topics(Complete).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("tbd")]
    [InlineData("?")]
    public void Heuristics_BlankTrigger_AsksWhatStartsIt(string trigger)
    {
        Topics(Complete with { TriggerDescription = trigger, Purpose = "Route purchase orders", StepsDescription = "Check the order details.\nThe manager approves it.\nIf rejected, tell the requester." })
            .Should().Contain(ClarificationTopics.Trigger);
    }

    [Fact]
    public void Heuristics_BlankTriggerButStepsSayWhen_DoesNotAsk()
    {
        Topics(Complete with { TriggerDescription = "", Purpose = "Runs when an employee submits a purchase request" })
            .Should().NotContain(ClarificationTopics.Trigger, "the purpose already says what starts it");
    }

    [Fact]
    public void Heuristics_ApprovalWithoutApprover_AsksWhoApproves()
    {
        var r = Complete with { StepsDescription = "Check the request details.\nIt needs approval before payment.\nIf rejected, close it." };
        var questions = NlClarificationService.DetectMissingEssentials(r);
        questions.Select(q => q.Topic).Should().Contain(ClarificationTopics.Approver);
        questions.Single(q => q.Topic == ClarificationTopics.Approver).Hint.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("The finance team approves the request.")]
    [InlineData("Priya approves the request.")]
    [InlineData("It is approved by Ahmad.")]
    [InlineData("Sign-off from the CFO is required.")]
    public void Heuristics_ApproverNamedInApprovalSentence_DoesNotAsk(string approvalSentence)
    {
        var r = Complete with { StepsDescription = $"Check the request details.\n{approvalSentence}\nIf rejected, close it." };
        Topics(r).Should().NotContain(ClarificationTopics.Approver);
    }

    [Fact]
    public void Heuristics_RoleNamedOnlyOutsideApprovalSentence_StillAsksApprover()
    {
        var r = Complete with { StepsDescription = "The finance team pays the invoice.\nThe invoice needs approval first.\nIf rejected, close it." };
        Topics(r).Should().Contain(ClarificationTopics.Approver);
    }

    [Fact]
    public void Heuristics_ApprovalWithoutRejectionPath_AsksWhatHappensOnRejection()
    {
        var r = Complete with { StepsDescription = "Validate the request.\nThe manager approves it.\nCreate the PO in SAP." };
        Topics(r).Should().Contain(ClarificationTopics.Rejection);
    }

    [Fact]
    public void Heuristics_NoApprovalAtAll_DoesNotAskApproverOrRejection()
    {
        var r = Complete with { StepsDescription = "Pull yesterday's orders from the database.\nBuild the summary.\nPost it to Slack." };
        Topics(r).Should().NotContain(ClarificationTopics.Approver).And.NotContain(ClarificationTopics.Rejection);
    }

    [Fact]
    public void Heuristics_BlankSystemsAndNoneMentioned_AsksWhichSystems()
    {
        var r = Complete with { SystemsAndAi = "", StepsDescription = "Validate the request.\nThe manager approves it.\nIf rejected, tell the requester." };
        Topics(r).Should().Contain(ClarificationTopics.Systems);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("N/A")]
    public void Heuristics_SystemsExplicitlyNone_DoesNotAsk(string systems)
    {
        var r = Complete with { SystemsAndAi = systems, StepsDescription = "Validate the request.\nThe manager approves it.\nIf rejected, tell the requester." };
        Topics(r).Should().NotContain(ClarificationTopics.Systems);
    }

    [Fact]
    public void Heuristics_EverythingMissing_ReturnsAtMostThreeQuestions_InPriorityOrder()
    {
        var r = new NlWorkflowRequest("Expense claims", "Handle expense claims", "", "Check the claim.\nIt needs approval.\nPay the claim.", "", "");

        var topics = Topics(r);

        topics.Should().HaveCount(NlClarificationService.MaxQuestions);
        topics.Should().Equal(ClarificationTopics.Trigger, ClarificationTopics.Approver, ClarificationTopics.Rejection);
    }

    [Fact]
    public void Heuristics_AnsweredTopics_AreNotAskedAgain()
    {
        var r = new NlWorkflowRequest("Expense claims", "Handle expense claims", "", "Check the claim.\nIt needs approval.\nPay the claim.", "", "",
            Clarifications:
            [
                new ClarificationAnswer(ClarificationTopics.Trigger, "What starts this workflow?", "Employee uploads a receipt"),
                new ClarificationAnswer(ClarificationTopics.Approver, "Who approves?", "Line manager"),
                new ClarificationAnswer(ClarificationTopics.Rejection, "If rejected?", ""),
            ]);

        var topics = Topics(r);

        topics.Should().NotContain(ClarificationTopics.Trigger).And.NotContain(ClarificationTopics.Approver);
        topics.Should().Contain(ClarificationTopics.Rejection, "a blank answer counts as skipped, not answered");
    }

    [Theory]
    [InlineData("Do the thing", true)]
    [InlineData("Check the claim and pay it", true)]
    [InlineData("Check the claim.\nPay the claim.", false)]
    [InlineData("1. Check the claim 2. Pay the claimant", false)]
    public void IsTooThinToJudge_CountsSteps(string steps, bool expected)
    {
        NlClarificationService.IsTooThinToJudge(Complete with { StepsDescription = steps }).Should().Be(expected);
    }

    // ─── Service: heuristics first, AI only when needed ──────────────────────

    [Fact]
    public async Task GetQuestions_HeuristicsFindGaps_NeverCallsAi()
    {
        var (sut, ai, metering, _) = BuildSut("""{"questions":[]}""");
        var r = Complete with { TriggerDescription = "", StepsDescription = "Check it.\nThe invoice needs approval.\nPay." };

        var result = await sut.GetQuestionsAsync(r, WorkspaceId);

        result.Questions.Should().NotBeEmpty();
        result.UsedAi.Should().BeFalse();
        result.TokensUsed.Should().Be(0);
        ai.VerifyNoOtherCalls();
        metering.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQuestions_CompleteDescription_ReturnsEmpty_WithoutAi()
    {
        var (sut, ai, _, _) = BuildSut("""{"questions":[]}""");

        var result = await sut.GetQuestionsAsync(Complete, WorkspaceId);

        result.Questions.Should().BeEmpty();
        ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQuestions_ThinDescriptionNoHeuristicGap_CallsAiOnce_MeteredViaResolvedModel()
    {
        const string aiJson = """{"questions":[{"topic":"other","question":"What does 'process the order' involve?"},{"topic":"systems","question":"Where are orders stored?"}]}""";
        var (sut, ai, metering, models) = BuildSut(aiJson);
        var thin = Complete with { StepsDescription = "Process the order for the customer" };

        var result = await sut.GetQuestionsAsync(thin, WorkspaceId);

        result.UsedAi.Should().BeTrue();
        result.TokensUsed.Should().Be(150);
        result.Questions.Select(q => q.Topic).Should().Equal(ClarificationTopics.Other, ClarificationTopics.Systems);
        result.Questions[0].Id.Should().Be("q-ai-1");
        models.Verify(m => m.ResolveModelConfigAsync(AiFunctionIds.NlYaml, WorkspaceId, It.IsAny<CancellationToken>()), Times.Once);
        ai.Verify(a => a.CompleteAsync(TestModel, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()), Times.Once);
        metering.Verify(m => m.RecordUsage(AiFunctionIds.NlYaml, TestModel.ModelId, TestModel.Provider, null, WorkspaceId, 100, 50, It.IsAny<decimal>()), Times.Once);
    }

    [Fact]
    public async Task GetQuestions_StubStyleNonJsonAiResponse_ReturnsNoQuestions()
    {
        var (sut, _, _, _) = BuildSut("Status summary:\nName: x");

        var result = await sut.GetQuestionsAsync(Complete with { StepsDescription = "Process the order for the customer" }, WorkspaceId);

        result.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetQuestions_AiNotConfigured_FallsBackWithoutThrowing()
    {
        var (sut, ai, _, _) = BuildSut("");
        ai.Setup(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .ThrowsAsync(new AiNotConfiguredException("anthropic", "m"));

        var result = await sut.GetQuestionsAsync(Complete with { StepsDescription = "Process the order for the customer" }, WorkspaceId);

        result.Questions.Should().BeEmpty();
        result.UsedAi.Should().BeFalse();
    }

    [Fact]
    public async Task GetQuestions_BudgetExhausted_SkipsAi()
    {
        var (sut, ai, _, _, budget) = BuildSutWithBudget("""{"questions":[{"topic":"other","question":"Q?"}]}""");
        budget.Setup(b => b.IsBudgetAvailableAsync(WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await sut.GetQuestionsAsync(Complete with { StepsDescription = "Process the order for the customer" }, WorkspaceId);

        result.Questions.Should().BeEmpty();
        ai.Verify(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void ParseAiQuestions_CapsAtThree_AndMapsUnknownTopicToOther()
    {
        const string json = """
            ```json
            {"questions":[{"topic":"weird","question":"A?"},{"topic":"approver","question":"B?"},{"topic":"trigger","question":"C?"},{"topic":"other","question":"D?"}]}
            ```
            """;

        var questions = NlClarificationService.ParseAiQuestions(json);

        questions.Should().HaveCount(3);
        questions[0].Topic.Should().Be(ClarificationTopics.Other);
        questions[1].Topic.Should().Be(ClarificationTopics.Approver);
    }

    // ─── Request validator ───────────────────────────────────────────────────

    [Fact]
    public void NlRequestValidator_TooManyClarifications_Fails()
    {
        var answers = Enumerable.Range(0, 6).Select(i => new ClarificationAnswer("other", $"Q{i}?", "A")).ToList();

        var result = new NlWorkflowRequestValidator().Validate(Complete with { Clarifications = answers });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("at most 5"));
    }

    [Fact]
    public void NlRequestValidator_ValidRequest_Passes()
    {
        new NlWorkflowRequestValidator().Validate(Complete).IsValid.Should().BeTrue();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static (NlClarificationService, Mock<IAiCompletionService>, Mock<IAiTokenMeteringService>, Mock<IModelResolutionService>) BuildSut(string aiText)
    {
        var (sut, ai, metering, models, _) = BuildSutWithBudget(aiText);
        return (sut, ai, metering, models);
    }

    private static (NlClarificationService, Mock<IAiCompletionService>, Mock<IAiTokenMeteringService>, Mock<IModelResolutionService>, Mock<IAiBudgetService>)
        BuildSutWithBudget(string aiText)
    {
        var ai = new Mock<IAiCompletionService>();
        var models = new Mock<IModelResolutionService>();
        var metering = new Mock<IAiTokenMeteringService>();
        var budget = new Mock<IAiBudgetService>();
        var cache = new Mock<ISemanticCacheService>();

        models.Setup(m => m.ResolveModelConfigAsync(AiFunctionIds.NlYaml, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(TestModel);
        ai.Setup(a => a.CompleteAsync(It.IsAny<ModelConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
            .ReturnsAsync(new AiCompletionResult(aiText, 100, 50));
        budget.Setup(b => b.IsBudgetAvailableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        cache.Setup(c => c.ComputeKey(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns("k");
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var sut = new NlClarificationService(ai.Object, models.Object, metering.Object, budget.Object, cache.Object,
            NullLogger<NlClarificationService>.Instance);
        return (sut, ai, metering, models, budget);
    }
}
