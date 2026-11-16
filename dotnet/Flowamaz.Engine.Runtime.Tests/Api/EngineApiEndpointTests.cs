using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Engine.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowamaz.Engine.Runtime.Tests.Api;

/// <summary>
/// The open HTTP endpoints (Community Edition phase 5a, part 2) on a real ASP.NET Core test server over the in-memory
/// runtime: workflow lifecycle, runs, an approval gate, access refusals, errors and the signed webhook trigger.
/// </summary>
public sealed class EngineApiEndpointTests : IAsyncLifetime
{
    private const string TestScheme = "Test";
    private const string UserHeader = "X-Test-User";

    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _webhookSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private const string GateYaml = """
        workflow: { id: leave, version: v1, name: Leave request }
        nodes:
          - { id: start, type: Trigger }
          - id: approve
            type: HumanGate
            label: Manager approval
            config: { assignee: manager@example.com, gate_label: Approve the leave }
          - { id: done, type: End }
        edges:
          - { id: e1, from: start, to: approve }
          - { id: e2, from: approve, to: done }
        """;

    /// <summary>Signs the request in as the user in <see cref="UserHeader"/>, if any.</summary>
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].ToString();
            if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("sub", user)], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["EngineApi:WebhookSecret"] = _webhookSecret;
        var databaseName = $"api-{Guid.NewGuid():N}";
        builder.Services.AddAuthentication(TestScheme).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestScheme, null);
        builder.Services.AddFlowamazEngineRuntime(builder.Configuration);
        builder.Services.AddFlowamazEnginePersistence(o => o.UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddFlowamazEngineWorker();
        builder.Services.AddFlowamazEngineApi(builder.Configuration);
        builder.Services.AddSingleton<IEngineApiAccess>(new SignedInUserAccess(_workspaceId));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.MapFlowamazEngineApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(UserHeader, _userId.ToString());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private string Ws => $"/api/v1/workspaces/{_workspaceId}";

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> CreateAndPublishAsync(string slug = "leave")
    {
        var created = await _client.PostAsJsonAsync($"{Ws}/workflows", new { name = "Leave request", slug, yamlContent = GateYaml });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await JsonOf(created)).GetProperty("id").GetGuid();
        var published = await _client.PostAsync($"{Ws}/workflows/{id}/publish", null);
        published.StatusCode.Should().Be(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        return id;
    }

    private async Task<JsonElement> WaitForRunAsync(Guid runId, string status)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var run = await JsonOf(await _client.GetAsync($"{Ws}/runs/{runId}"));
            if (run.GetProperty("status").GetString() == status) return run;
            if (DateTime.UtcNow > until) throw new TimeoutException($"Run {runId} is still {run.GetProperty("status")}.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Create_publish_run_approve_and_read_the_run_back()
    {
        var workflowId = await CreateAndPublishAsync();

        var list = await JsonOf(await _client.GetAsync($"{Ws}/workflows"));
        list.GetProperty("total").GetInt32().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("status").GetString().Should().Be("Published");
        (await JsonOf(await _client.GetAsync($"{Ws}/workflows/{workflowId}/versions"))).GetArrayLength().Should().Be(1);

        var started = await _client.PostAsJsonAsync($"{Ws}/workflows/{workflowId}/runs", new { payload = new { days = 3 } });
        started.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var runId = (await JsonOf(started)).GetProperty("instanceId").GetGuid();

        await WaitForRunAsync(runId, "Waiting");
        var gates = await JsonOf(await _client.GetAsync($"{Ws}/gates"));
        gates.GetArrayLength().Should().Be(1);
        gates[0].GetProperty("gateLabel").GetString().Should().Be("Approve the leave");

        var decided = await _client.PostAsJsonAsync($"{Ws}/gates/{runId}/approve/decide", new { decision = "approved", note = "ok" });
        decided.StatusCode.Should().Be(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());

        (await JsonOf(await _client.GetAsync($"{Ws}/gates"))).GetArrayLength().Should().Be(0, "nothing is waiting any more");
        var all = await JsonOf(await _client.GetAsync($"{Ws}/gates?status=all"));
        all.GetArrayLength().Should().Be(1);
        all[0].GetProperty("decision").GetString().Should().Be("Approved");
        (await _client.GetAsync($"{Ws}/gates?status=nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var done = await WaitForRunAsync(runId, "Completed");
        done.GetProperty("triggerType").GetString().Should().Be("Api");
        var timeline = await JsonOf(await _client.GetAsync($"{Ws}/runs/{runId}/timeline"));
        timeline.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("label").GetString())
            .Should().Contain("Manager approval");
        var runs = await JsonOf(await _client.GetAsync($"{Ws}/runs?status=completed"));
        runs.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().Contain(runId);
    }

    [Fact]
    public async Task Save_a_draft_then_delete_it()
    {
        var created = await JsonOf(await _client.PostAsJsonAsync($"{Ws}/workflows", new { name = "Draft", slug = "draft", yamlContent = GateYaml }));
        var id = created.GetProperty("id").GetGuid();

        var saved = await _client.PutAsJsonAsync($"{Ws}/workflows/{id}", new { yamlContent = GateYaml.Replace("Leave request", "Holiday request") });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonOf(saved)).GetProperty("yamlContent").GetString().Should().Contain("Holiday request");

        (await _client.DeleteAsync($"{Ws}/workflows/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"{Ws}/workflows/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancel_a_waiting_run()
    {
        var workflowId = await CreateAndPublishAsync();
        var runId = (await JsonOf(await _client.PostAsync($"{Ws}/workflows/{workflowId}/runs", null))).GetProperty("instanceId").GetGuid();
        await WaitForRunAsync(runId, "Waiting");

        var cancelled = await _client.PostAsync($"{Ws}/runs/{runId}/cancel", null);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonOf(cancelled)).GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Business_errors_come_back_with_their_code_and_status()
    {
        await CreateAndPublishAsync();

        var duplicate = await _client.PostAsJsonAsync($"{Ws}/workflows", new { name = "Again", slug = "leave", yamlContent = GateYaml });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonOf(duplicate)).GetProperty("code").GetString().Should().Be("WORKFLOW_SLUG_TAKEN");

        var missing = await _client.PostAsJsonAsync($"{Ws}/workflows", new { slug = "x", yamlContent = GateYaml });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonOf(missing)).GetProperty("message").GetString().Should().Contain("'name' is required");

        var broken = await JsonOf(await _client.PostAsJsonAsync($"{Ws}/workflows", new { name = "Broken", slug = "broken", yamlContent = "nodes: [" }));
        var publish = await _client.PostAsync($"{Ws}/workflows/{broken.GetProperty("id").GetGuid()}/publish", null);
        ((int)publish.StatusCode).Should().BeInRange(400, 499);
        (await JsonOf(publish)).GetProperty("code").GetString().Should().NotBeNullOrEmpty();

        (await _client.GetAsync($"{Ws}/runs?status=nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unknownRun = await _client.GetAsync($"{Ws}/runs/{Guid.NewGuid()}");
        unknownRun.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonOf(unknownRun)).GetProperty("code").GetString().Should().Be("RUN_NOT_FOUND");
    }

    [Fact]
    public async Task Without_sign_in_it_is_401_and_another_workspace_is_403()
    {
        using var anonymous = _app.GetTestClient();
        (await anonymous.GetAsync($"{Ws}/workflows")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var other = await _client.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}/workflows");
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonOf(other)).GetProperty("code").GetString().Should().Be("ENGINE_ACCESS_DENIED");
    }

    private HttpRequestMessage Webhook(Guid workflowId, string body, string? secret, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/hooks/workspaces/{_workspaceId}/workflows/{workflowId}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (secret is not null)
            request.Headers.Add("X-Flowamaz-Signature",
                "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task A_signed_webhook_starts_a_run_once_per_idempotency_key()
    {
        var workflowId = await CreateAndPublishAsync();
        using var anonymous = _app.GetTestClient(); // webhooks need no sign-in, only the signature

        var first = await anonymous.SendAsync(Webhook(workflowId, """{"days":2}""", _webhookSecret, "evt-1"));
        var again = await anonymous.SendAsync(Webhook(workflowId, """{"days":2}""", _webhookSecret, "evt-1"));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        var runId = (await JsonOf(first)).GetProperty("instanceId").GetGuid();
        (await JsonOf(again)).GetProperty("instanceId").GetGuid().Should().Be(runId);
        (await WaitForRunAsync(runId, "Waiting")).GetProperty("triggerType").GetString().Should().Be("Webhook");
    }

    [Fact]
    public async Task A_webhook_with_a_wrong_or_missing_signature_or_bad_json_is_refused()
    {
        var workflowId = await CreateAndPublishAsync();
        using var anonymous = _app.GetTestClient();

        (await anonymous.SendAsync(Webhook(workflowId, "{}", "not-the-secret"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.SendAsync(Webhook(workflowId, "{}", null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.SendAsync(Webhook(workflowId, "not json", _webhookSecret))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var runs = await JsonOf(await _client.GetAsync($"{Ws}/runs"));
        runs.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("sha256=", false)]
    [InlineData("sha256=zz", false)]
    [InlineData("md5=abcd", false)]
    [InlineData("", false)]
    public void Signature_check_rejects_malformed_headers(string header, bool expected) =>
        EngineApiEndpoints.SignatureMatches(header, "secret", "{}"u8.ToArray()).Should().Be(expected);

    [Fact]
    public void Signature_check_accepts_the_bodys_hmac_in_any_case()
    {
        var body = """{"a":1}"""u8.ToArray();
        var hex = Convert.ToHexString(HMACSHA256.HashData("secret"u8.ToArray(), body));
        EngineApiEndpoints.SignatureMatches($"sha256={hex}", "secret", body).Should().BeTrue();
        EngineApiEndpoints.SignatureMatches($"SHA256={hex.ToLowerInvariant()}", "secret", body).Should().BeTrue();
    }

    [Fact]
    public async Task Mapping_without_an_access_rule_fails_at_startup_with_what_to_do()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var act = () => app.MapFlowamazEngineApi();

        act.Should().Throw<InvalidOperationException>().WithMessage("*register an IEngineApiAccess*");
    }

    [Fact]
    public void A_short_webhook_secret_is_refused_at_startup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["EngineApi:WebhookSecret"] = "short";
        builder.Services.AddFlowamazEngineApi(builder.Configuration);
        using var provider = builder.Services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<EngineApiOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*at least 32 characters*");
    }
}
