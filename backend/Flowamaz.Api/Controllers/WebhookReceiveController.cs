using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowamaz.Api.Controllers;

/// <summary>
/// Public webhook receive endpoint. No JWT — requests authenticate with an HMAC-SHA256 signature
/// over the raw body (constant-time verified in <see cref="IWebhookService"/>). Rate limited to
/// 100/min per endpoint id (not per IP — senders may have dynamic IPs). The <c>/webhooks</c> path
/// prefix is exempt from <c>JwtAuthMiddleware</c>.
/// </summary>
[ApiController]
[Route("webhooks")]
[AllowAnonymous]
public sealed class WebhookReceiveController : ControllerBase
{
    private const int RateLimitPerMinute = 100;
    private const int RateWindowSeconds = 60;

    private readonly IWebhookService _webhooks;
    private readonly IRateLimitService _rateLimit;
    private readonly ILogger<WebhookReceiveController> _logger;

    public WebhookReceiveController(
        IWebhookService webhooks, IRateLimitService rateLimit, ILogger<WebhookReceiveController> logger)
    {
        _webhooks = webhooks;
        _rateLimit = rateLimit;
        _logger = logger;
    }

    [HttpPost("{endpointId:guid}")]
    public async Task<IActionResult> Receive(Guid endpointId, [FromQuery] bool sync, CancellationToken cancellationToken)
    {
        var withinBudget = await _rateLimit.CheckAndIncrementAsync(
            $"webhook:rate:{endpointId}", RateLimitPerMinute, RateWindowSeconds, cancellationToken);
        if (!withinBudget)
        {
            _logger.LogWarning("WebhookReceiveController.Receive rate limited endpoint={EndpointId}", endpointId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "rate_limited",
                message = $"Too many requests for this webhook. Limit is {RateLimitPerMinute} per minute. Retry shortly.",
            });
        }

        Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }
        Request.Body.Position = 0;

        var signature = FirstHeader("X-Flowamaz-Signature") ?? FirstHeader("X-Hub-Signature-256");
        var idempotencyKey = FirstHeader("X-Idempotency-Key");

        try
        {
            var result = await _webhooks.TriggerFromWebhookAsync(
                endpointId, rawBody, signature, idempotencyKey, waitForTerminal: sync, cancellationToken);

            if (sync)
            {
                return result.IsTerminal
                    ? Ok(new { instanceId = result.InstanceId, status = result.Status })
                    : StatusCode(StatusCodes.Status408RequestTimeout, new
                    {
                        instanceId = result.InstanceId,
                        status = result.Status,
                        message = "The workflow did not reach a terminal state within 30 seconds. " +
                                  "Poll the instance status endpoint to follow its progress.",
                    });
            }

            return Accepted(new { instanceId = result.InstanceId, status = "accepted" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = "invalid_signature", message = ex.Message });
        }
        catch (WorkflowNotPublishedException ex)
        {
            return Conflict(new
            {
                error = "workflow_not_published",
                message = ex.Message + " Publish the workflow before sending live webhook traffic.",
            });
        }
    }

    private string? FirstHeader(string name) =>
        Request.Headers.TryGetValue(name, out var values) ? values.ToString() : null;
}
