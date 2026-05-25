using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Workflow.Workers;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Flowamaz.Tests.Unit.Workflow;

/// <summary>
/// HttpActionWorker behaviour (prompt 02-03): 2xx → success with parsed output, 5xx → retries up to
/// MaxAttempts with backoff, 4xx → no retry, and a timeout → non-retryable failure.
/// </summary>
public class HttpActionWorkerTests
{
    private readonly Mock<IVariableEvaluationService> _varEval = new();

    public HttpActionWorkerTests()
    {
        // Passthrough interpolation — templates contain no variables here.
        _varEval.Setup(v => v.InterpolateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, CancellationToken>((_, template, _) => Task.FromResult(template));
    }

    [Fact]
    public async Task Executes_successfully_and_parses_json_output()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));
        var worker = NewWorker(handler);

        var result = await worker.ExecuteAsync(Context(NewNode()), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNull();
        result.Output!.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Retries_on_503_up_to_max_attempts_then_fails()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var worker = NewWorker(handler);
        var node = NewNode(retry: new RetryPolicy(MaxAttempts: 3, BackoffSeconds: 0, BackoffMultiplier: 1));

        var result = await worker.ExecuteAsync(Context(node), CancellationToken.None);

        result.Success.Should().BeFalse();
        handler.CallCount.Should().Be(3, "the worker retries transient 5xx up to MaxAttempts");
    }

    [Fact]
    public async Task Does_not_retry_on_400()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var worker = NewWorker(handler);
        var node = NewNode(retry: new RetryPolicy(MaxAttempts: 3, BackoffSeconds: 0, BackoffMultiplier: 1));

        var result = await worker.ExecuteAsync(Context(node), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        handler.CallCount.Should().Be(1, "4xx is a client error — not retried");
    }

    [Fact]
    public async Task Timeout_returns_non_retryable_failure()
    {
        var handler = new StubHandler(_ => null); // null → hang until the timeout CTS cancels
        var worker = NewWorker(handler);
        var node = NewNode(timeout: new TimeoutPolicy(TimeoutSeconds: 1, OnTimeoutNodeId: null));

        var result = await worker.ExecuteAsync(Context(node), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    private HttpActionWorker NewWorker(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        return new HttpActionWorker(factory.Object, _varEval.Object, NullLogger<HttpActionWorker>.Instance);
    }

    private static SfgNode NewNode(RetryPolicy? retry = null, TimeoutPolicy? timeout = null) => new(
        "act", NodeType.Action, "Act",
        JsonDocument.Parse("""{"method":"GET","url":"https://api.test/resource"}"""),
        retry, timeout, null);

    private static NodeExecutionContext Context(SfgNode node) => new(
        Guid.NewGuid(), Guid.NewGuid(), node,
        new WorkflowGraph("w", "v1", [node], [], new WorkflowMetadata("w", null)),
        new Dictionary<string, JsonElement>(), "lease-1");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<int, HttpResponseMessage?> responder) : HttpMessageHandler
    {
        public int CallCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = responder(CallCount);
            if (response is null)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return response!;
        }
    }
}
