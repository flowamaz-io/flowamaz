using System.Net;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Flowamaz.Tests.Unit.Connectors;

[Trait("Category", "Connectors")]
public class SlackConnectorHandlerTests
{
    private static IHttpClientFactory BuildFactory(HttpStatusCode status, string responseBody)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody)
            });

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factoryMock.Object;
    }

    [Fact]
    public async Task SendMessage_PostsToSlackChatPostMessage()
    {
        // Arrange
        var capturedRequest = default(HttpRequestMessage);
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"ts\":\"1234567890.123\"}")
            });

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var handler = new SlackSendMessageHandler(factoryMock.Object, NullLogger<SlackSendMessageHandler>.Instance);
        var input = JsonDocument.Parse("{\"channel\":\"#general\",\"text\":\"Hello World\"}");

        // Act
        var result = await handler.ExecuteAsync(input, null, CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://slack.com/api/chat.postMessage");
        capturedRequest.Method.Should().Be(HttpMethod.Post);

        var resultRoot = result.RootElement;
        resultRoot.GetProperty("ok").GetBoolean().Should().BeTrue();
        resultRoot.GetProperty("ts").GetString().Should().Be("1234567890.123");
    }

    [Fact]
    public async Task PostApprovalMessage_BlockKitHasApproveAndRejectButtonsWithGateDecisionId()
    {
        // Arrange
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var handler = new SlackPostApprovalMessageHandler(factoryMock.Object, NullLogger<SlackPostApprovalMessageHandler>.Instance);
        var gateDecisionId = Guid.NewGuid().ToString();
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            channel = "#approvals",
            gate_decision_id = gateDecisionId,
            workflow_name = "Invoice Approval",
            gate_summary = "Please review the invoice."
        }));

        // Act
        var result = await handler.ExecuteAsync(input, null, CancellationToken.None);

        // Assert
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody!.Should().Contain(gateDecisionId);
        capturedBody.Should().Contain("approve_gate");
        capturedBody.Should().Contain("reject_gate");
        capturedBody.Should().Contain("Approve");
        capturedBody.Should().Contain("Reject");

        var resultRoot = result.RootElement;
        resultRoot.GetProperty("ok").GetBoolean().Should().BeTrue();
    }
}
