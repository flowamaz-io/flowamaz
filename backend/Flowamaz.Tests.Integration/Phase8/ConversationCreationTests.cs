using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Tests.Integration.Common;

namespace Flowamaz.Tests.Integration.Phase8;

/// <summary>
/// Conversation → Workflow review step (FUNCTIONAL.md §6.4, §8.4) through the API. The fixture runs AI on the local
/// stub, which echoes the prompt instead of answering in JSON, so extraction fails the way a confused model would.
/// </summary>
[Collection("api")]
public sealed class ConversationCreationTests : ApiTestBase
{
    public ConversationCreationTests(IntegrationApiFixture fixture) : base(fixture) { }

    private static string U(string slug) => $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

    [Fact]
    public async Task Preview_checks_its_input_and_explains_an_unreadable_AI_answer()
    {
        var owner = await RegisterOwnerAsync(U("conv-preview"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Purchasing", U("conv-preview-ws"));

        var empty = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/conversation/preview", new { text = " ", sourceType = "slack" });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(empty)).Should().StartWith("Paste a Slack thread");

        var badSource = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/conversation/preview", new { text = "a: b", sourceType = "fax" });
        badSource.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(badSource)).Should().Contain("Use slack, teams, email, or generic");

        var unreadable = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/conversation/preview",
            new { text = "Anita: please approve\nRajan: up to $5,000", sourceType = "generic" });
        unreadable.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = JsonDocument.Parse(await unreadable.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("code").GetString().Should().Be("CONVERSATION_EXTRACTION_FAILED");
        body.GetProperty("message").GetString().Should().Contain("try Extract again");
    }

    [Fact]
    public async Task Create_rejects_a_reviewed_extraction_with_every_step_removed()
    {
        var owner = await RegisterOwnerAsync(U("conv-create"));
        var ws = await CreateWorkspaceAsync(owner.Client, "Purchasing", U("conv-create-ws"));

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{ws}/workflows/create", new
        {
            name = "Purchase approval",
            method = "conversation",
            conversationExtraction = new
            {
                workflowName = "Purchase approval",
                summary = "",
                trigger = "",
                participants = Array.Empty<object>(),
                steps = Array.Empty<object>(),
                decisions = Array.Empty<object>(),
                approvers = Array.Empty<object>(),
                agreements = Array.Empty<string>(),
                systems = Array.Empty<string>(),
                openQuestions = Array.Empty<string>(),
                confidence = 0.9,
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessageAsync(response)).Should().Be("Every step was removed. Keep or add at least one step, or describe the process in plain English instead.");
    }
}
