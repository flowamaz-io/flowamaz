using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Flowamaz.Application.Connectors;
using Flowamaz.Application.Connectors.Handlers;
using Flowamaz.Application.Connectors.Security;
using Flowamaz.Application.Connectors.Services;
using Flowamaz.Application.Workflow.Scheduling;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Exceptions;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Core.Interfaces.Workflow;
using Flowamaz.Core.Models;
using Flowamaz.Core.Workflow;
using Flowamaz.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace Flowamaz.Tests.Unit.Connectors;

/// <summary>Connector-as-workflow-step wiring: catalog, executor, credentials, egress, interpolation.</summary>
[Trait("Category", "Connectors")]
public class ConnectorWiringTests
{
    // ── Catalog ↔ handlers ───────────────────────────────────────────────────

    private static IEnumerable<IConnectorOperationHandler> AllHandlers() =>
        typeof(SlackHandlerBase).Assembly.GetTypes()
            .Where(t => typeof(IConnectorOperationHandler).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            // Ids are constant expression-bodied properties, so an uninitialised instance can report them.
            .Select(t => (IConnectorOperationHandler)RuntimeHelpers.GetUninitializedObject(t));

    [Fact]
    public void EveryCatalogOperation_HasAHandler_AndEveryHandler_IsInTheCatalog()
    {
        var handlers = AllHandlers().Select(h => (h.ConnectorId, h.OperationId)).ToHashSet();
        var catalog = OfficialConnectorCatalog.Connectors
            .SelectMany(c => c.Operations.Select(o => (c.ConnectorId, o.Id))).ToHashSet();

        catalog.Except(handlers).Should().BeEmpty("every published operation must be executable");
        handlers.Except(catalog).Should().BeEmpty("every handler must be published so the UI and validation know it");
    }

    [Fact]
    public void AllThirteenOfficialConnectors_AreInTheCatalog_WithNonEmptyManifests()
    {
        OfficialConnectorCatalog.Connectors.Should().HaveCount(13);
        foreach (var c in OfficialConnectorCatalog.Connectors.Where(c => c.Triggers is null || c.Operations.Count > 0))
        {
            using var manifest = JsonDocument.Parse(OfficialConnectorCatalog.ManifestJson(c.ConnectorId));
            manifest.RootElement.GetProperty("operations").GetArrayLength().Should().BeGreaterThan(0, c.ConnectorId);
        }
    }

    // ── Node config parsing ─────────────────────────────────────────────────

    [Fact]
    public void NodeConfig_ParsesSpecKeys()
    {
        var cfg = JsonDocument.Parse("""{"connector_id":"slack","operation":"send-message","credential":"Ops Slack","inputs":{"channel":"#ops"}}""");
        var parsed = ConnectorNodeConfig.TryParse(cfg.RootElement)!;
        parsed.ConnectorId.Should().Be("slack");
        parsed.OperationId.Should().Be("send-message");
        parsed.CredentialRef.Should().Be("Ops Slack");
        parsed.Inputs.GetProperty("channel").GetString().Should().Be("#ops");
    }

    [Fact]
    public void NodeConfig_ParsesLegacyCanvasKeys_AndIgnoresRawHttpNodes()
    {
        var legacy = ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector":"github","action":"create-issue","fields":{"title":"x"}}""").RootElement)!;
        legacy.ConnectorId.Should().Be("github");
        legacy.OperationId.Should().Be("create-issue");
        legacy.Inputs.GetProperty("title").GetString().Should().Be("x");

        ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"url":"https://example.com"}""").RootElement).Should().BeNull();
    }

    // ── Executor ────────────────────────────────────────────────────────────

    private static NodeExecutionContext Ctx(string configJson, Guid? ws = null) => new(
        Guid.NewGuid(), ws ?? Guid.NewGuid(),
        new SfgNode("step", NodeType.Action, "Step", JsonDocument.Parse(configJson), null, null, null),
        new WorkflowGraph("wf", "v1", [], [], new WorkflowMetadata("wf", null)), new Dictionary<string, JsonElement>(), "lease");

    private sealed class Fixture
    {
        public Mock<IConnectorOperationHandlerRegistry> Registry { get; } = new();
        public Mock<IConnectorCredentialResolver> Credentials { get; } = new();
        public Mock<IVariableEvaluationService> Variables { get; } = new();
        public ConnectorCallContext CallContext { get; } = new();
        public ConnectorActionExecutor Executor => new(Registry.Object, Credentials.Object, Variables.Object, CallContext,
            NullLogger<ConnectorActionExecutor>.Instance);

        public Mock<IConnectorOperationHandler> Handler(string connector, string op)
        {
            var h = new Mock<IConnectorOperationHandler>();
            h.SetupGet(x => x.ConnectorId).Returns(connector);
            h.SetupGet(x => x.OperationId).Returns(op);
            Registry.Setup(r => r.Get(connector, op)).Returns(h.Object);
            return h;
        }
    }

    [Fact]
    public async Task Executor_ResolvesCredential_InterpolatesInputs_AndSetsWorkspaceContext()
    {
        var f = new Fixture();
        var ws = Guid.NewGuid();
        var cred = new ConnectorCredential(Guid.NewGuid(), "Ops Slack", "OAuth2AuthCode", "xoxb");
        f.Credentials.Setup(c => c.ResolveAsync(ws, "slack", "Ops Slack", It.IsAny<CancellationToken>())).ReturnsAsync(cred);
        f.Variables.Setup(v => v.InterpolateValueAsync(It.IsAny<Guid>(), "Order {{ variables.order_id }} approved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.SerializeToElement("Order 42 approved"));
        JsonElement seenInput = default;
        var handler = f.Handler("slack", "send-message");
        handler.Setup(h => h.ExecuteAsync(It.IsAny<JsonDocument>(), cred, It.IsAny<CancellationToken>()))
            .Callback<JsonDocument, ConnectorCredential?, CancellationToken>((i, _, _) => seenInput = i.RootElement.Clone())
            .ReturnsAsync(JsonDocument.Parse("""{"ok":true}"""));

        var result = await f.Executor.ExecuteAsync(
            Ctx("""{"connector_id":"slack","operation":"send-message","credential":"Ops Slack","inputs":{"channel":"#ops","text":"Order {{ variables.order_id }} approved"}}""", ws),
            ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector_id":"slack","operation":"send-message","credential":"Ops Slack","inputs":{"channel":"#ops","text":"Order {{ variables.order_id }} approved"}}""").RootElement)!,
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        seenInput.GetProperty("text").GetString().Should().Be("Order 42 approved");
        seenInput.GetProperty("channel").GetString().Should().Be("#ops");
        f.CallContext.WorkspaceId.Should().Be(ws);
    }

    [Fact]
    public async Task Executor_MissingRequiredInput_IsFatal_AndNamesTheField()
    {
        var f = new Fixture();
        var handler = f.Handler("slack", "send-message");
        var node = ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector_id":"slack","operation":"send-message","inputs":{"channel":"#ops"}}""").RootElement)!;

        var result = await f.Executor.ExecuteAsync(Ctx("{}"), node, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.ErrorMessage.Should().Contain("text");
        handler.Verify(h => h.ExecuteAsync(It.IsAny<JsonDocument>(), It.IsAny<ConnectorCredential?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Executor_RequiredCredentialMissing_IsFatal_WithSetupHint()
    {
        var f = new Fixture();
        f.Handler("github", "create-issue");
        f.Credentials.Setup(c => c.ResolveAsync(It.IsAny<Guid>(), "github", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectorCredential?)null);
        var node = ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector_id":"github","operation":"create-issue","inputs":{"owner":"o","repo":"r","title":"t"}}""").RootElement)!;

        var result = await f.Executor.ExecuteAsync(Ctx("{}"), node, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Settings → Credentials");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Executor_MapsConnectorOperationException_ToRetryability(bool retryable)
    {
        var f = new Fixture();
        f.Handler("http-rest", "GET").Setup(h => h.ExecuteAsync(It.IsAny<JsonDocument>(), It.IsAny<ConnectorCredential?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConnectorOperationException("vendor said no", retryable));
        var node = ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector_id":"http-rest","operation":"GET","inputs":{"url":"https://example.com"}}""").RootElement)!;

        var result = await f.Executor.ExecuteAsync(Ctx("{}"), node, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().Be(retryable);
        result.ErrorMessage.Should().Contain("vendor said no");
    }

    [Fact]
    public async Task Executor_UnknownOperationOnOfficialConnector_ListsAvailableOperations()
    {
        var f = new Fixture();
        var node = ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector_id":"slack","operation":"send-fax"}""").RootElement)!;

        var result = await f.Executor.ExecuteAsync(Ctx("{}"), node, CancellationToken.None);

        result.ErrorMessage.Should().Contain("send-fax").And.Contain("send-message").And.NotContain("post-approval-message");
    }

    [Fact]
    public async Task Executor_CredentialProblem_IsFatal()
    {
        var f = new Fixture();
        f.Handler("slack", "send-message");
        f.Credentials.Setup(c => c.ResolveAsync(It.IsAny<Guid>(), "slack", "Gone", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConnectorCredentialException("Credential 'Gone' was not found"));
        var node = ConnectorNodeConfig.TryParse(JsonDocument.Parse("""{"connector_id":"slack","operation":"send-message","credential":"Gone","inputs":{"channel":"#a","text":"b"}}""").RootElement)!;

        var result = await f.Executor.ExecuteAsync(Ctx("{}"), node, CancellationToken.None);

        result.ShouldRetry.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Gone");
    }

    // ── Credential selection ────────────────────────────────────────────────

    private static readonly (Guid, string, string) A = (Guid.NewGuid(), "Ops Slack", "OAuth2AuthCode");
    private static readonly (Guid, string, string) B = (Guid.NewGuid(), "Sales Slack", "OAuth2AuthCode");

    [Fact]
    public void Resolver_NoRef_SingleCredential_IsUsed_NoneIsNull_SeveralIsAnError()
    {
        ConnectorCredentialResolver.Select([A], "slack", null).Should().Be(A);
        ConnectorCredentialResolver.Select([], "slack", null).Should().BeNull();
        var act = () => ConnectorCredentialResolver.Select([A, B], "slack", null);
        act.Should().Throw<ConnectorCredentialException>().WithMessage("*2 slack credentials*");
    }

    [Fact]
    public void Resolver_ByNameOrId_AndMissingRefIsAnError()
    {
        ConnectorCredentialResolver.Select([A, B], "slack", "sales slack").Should().Be(B);
        ConnectorCredentialResolver.Select([A, B], "slack", A.Item1.ToString()).Should().Be(A);
        var act = () => ConnectorCredentialResolver.Select([A, B], "slack", "Marketing");
        act.Should().Throw<ConnectorCredentialException>().WithMessage("*Marketing*not found*");
    }

    // ── Egress guard ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("169.254.169.254", true)]   // cloud metadata
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.5", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]   // IPv4-mapped private
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void EgressGuard_BlocksInternalAddresses(string ip, bool blocked) =>
        EgressGuard.IsBlocked(IPAddress.Parse(ip)).Should().Be(blocked);

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://x")]
    [InlineData("not a url")]
    [InlineData("")]
    public void EgressGuard_RejectsNonHttpUrls(string url)
    {
        var act = () => EgressGuard.RequireHttpUrl(url);
        act.Should().Throw<ConnectorOperationException>();
    }

    [Fact]
    public async Task EgressGuard_Handler_RefusesToConnectToLoopback()
    {
        using var client = new HttpClient(EgressGuard.CreateHandler(allowPrivateNetworks: false));
        var act = () => client.GetAsync("http://127.0.0.1:1/");
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("private or internal");
    }

    // ── Variable paths ──────────────────────────────────────────────────────

    [Fact]
    public void VariableResolve_SupportsSpecPrefix_HyphenatedNodeOutputs_AndArrayIndexes()
    {
        var vars = new Dictionary<string, JsonElement>
        {
            ["amount"] = JsonSerializer.SerializeToElement(1500),
            ["create-issue.output"] = JsonSerializer.SerializeToElement(new { number = 7, labels = new[] { "bug", "p1" } }),
        };

        VariableEvaluationService.Resolve(vars, "variables.amount")!.Value.GetInt32().Should().Be(1500);
        VariableEvaluationService.Resolve(vars, "amount")!.Value.GetInt32().Should().Be(1500);
        VariableEvaluationService.Resolve(vars, "create-issue.output.number")!.Value.GetInt32().Should().Be(7);
        VariableEvaluationService.Resolve(vars, "create-issue.output.labels.1")!.Value.GetString().Should().Be("p1");
        VariableEvaluationService.Resolve(vars, "create-issue.output.missing").Should().BeNull();
        VariableEvaluationService.Resolve(vars, "nope").Should().BeNull();
    }

    // ── Cron ────────────────────────────────────────────────────────────────

    [Fact]
    public void Cron_ParsesStandardAndTimeZone_RejectsGarbage_AndPerSecondSchedules()
    {
        CronSchedule.TryParse("0 9 * * 1-5", "Asia/Kuala_Lumpur", out var ok, out _).Should().BeTrue();
        CronSchedule.NextOccurrences(ok!, DateTime.UtcNow, 3).Should().HaveCount(3).And.BeInAscendingOrder();

        CronSchedule.TryParse("61 * * * *", null, out _, out var bad).Should().BeFalse();
        bad.Should().NotBeEmpty();
        CronSchedule.TryParse("0 9 * * *", "Mars/Olympus", out _, out var tzErr).Should().BeFalse();
        tzErr.Should().Contain("time zone");
        CronSchedule.TryParse("* * * * * *", null, out _, out var tooFast).Should().BeFalse();
        tooFast.Should().Contain("once per minute");
    }

    // ── Handlers: secrets, escaping, signing, file jail ─────────────────────

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public async Task Sql_ConnectionStringInInputs_IsRejected_AndCredentialRequired()
    {
        var handler = new PostgreSqlQueryHandler(Config(), NullLogger<PostgreSqlQueryHandler>.Instance);

        var inYaml = () => handler.ExecuteAsync(JsonDocument.Parse("""{"sql":"select 1","connection_string":"Host=db;Password=p"}"""), null, default);
        (await inYaml.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("must not be set");

        var noCred = () => handler.ExecuteAsync(JsonDocument.Parse("""{"sql":"select 1"}"""), null, default);
        (await noCred.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("Credentials");
    }

    [Fact]
    public async Task Sql_PrivateDatabaseHost_IsBlockedUnlessAllowed()
    {
        var handler = new PostgreSqlQueryHandler(Config(), NullLogger<PostgreSqlQueryHandler>.Instance);
        var cred = new ConnectorCredential(Guid.NewGuid(), "db", "ConnectionString", "Host=10.0.0.5;Username=u;Password=p;Database=d");

        var act = () => handler.ExecuteAsync(JsonDocument.Parse("""{"sql":"select 1"}"""), cred, default);

        (await act.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("private or internal");
    }

    [Fact]
    public async Task Email_PlainTextBody_IsHtmlEncoded()
    {
        var email = new Mock<IEmailService>();
        string? html = null;
        email.Setup(e => e.SendAsync("a@b.co", "Hi", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, CancellationToken>((_, _, h, _, _) => html = h)
            .ReturnsAsync(true);
        var handler = new EmailSendHandler(email.Object, NullLogger<EmailSendHandler>.Instance);

        await handler.ExecuteAsync(JsonDocument.Parse("""{"to":"a@b.co","subject":"Hi","body":"<script>x</script>"}"""), null, default);

        html.Should().Contain("&lt;script&gt;").And.NotContain("<script>");
    }

    [Fact]
    public async Task Email_ProviderRefusal_IsARetryableFailure()
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = new EmailSendHandler(email.Object, NullLogger<EmailSendHandler>.Instance);

        var act = () => handler.ExecuteAsync(JsonDocument.Parse("""{"to":"a@b.co","subject":"Hi","body":"x"}"""), null, default);

        (await act.Should().ThrowAsync<ConnectorOperationException>()).Which.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task WebhookEmit_SignsTimestampAndBody_WithCredentialSecret()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var http = new Mock<HttpMessageHandler>();
        http.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => { sent = r; body = r.Content!.ReadAsStringAsync().Result; })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(http.Object));
        var handler = new WebhookEmitHandler(factory.Object, NullLogger<WebhookEmitHandler>.Instance);
        var cred = new ConnectorCredential(Guid.NewGuid(), "hook", "HmacSecret", "s3cret");

        await handler.ExecuteAsync(JsonDocument.Parse("""{"url":"https://hooks.example.com/x","payload":{"a":1}}"""), cred, default);

        var ts = sent!.Headers.GetValues("X-Flowamaz-Timestamp").Single();
        sent.Headers.GetValues("X-Flowamaz-Signature").Single().Should().Be($"sha256={WebhookEmitHandler.Sign($"{ts}.{body}", "s3cret")}");
    }

    [Fact]
    public async Task WebhookEmit_SecretInInputs_IsRejected()
    {
        var handler = new WebhookEmitHandler(Mock.Of<IHttpClientFactory>(), NullLogger<WebhookEmitHandler>.Instance);
        var act = () => handler.ExecuteAsync(JsonDocument.Parse("""{"url":"https://x.example","payload":{},"secret":"s"}"""), null, default);
        await act.Should().ThrowAsync<ConnectorOperationException>();
    }

    [Fact]
    public async Task FileSystem_IsJailedToTheWorkspaceFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "fa-fs-" + Guid.NewGuid().ToString("N"));
        var config = Config((FileSystemHandlerBase.RootPathKey, root));
        var ctx = new ConnectorCallContext { WorkspaceId = Guid.NewGuid() };
        var write = new FileSystemWriteHandler(config, ctx, NullLogger<FileSystemWriteHandler>.Instance);
        var read = new FileSystemReadHandler(config, ctx, NullLogger<FileSystemReadHandler>.Instance);
        try
        {
            await write.ExecuteAsync(JsonDocument.Parse("""{"path":"reports/a.txt","content":"hello"}"""), null, default);
            var doc = await read.ExecuteAsync(JsonDocument.Parse("""{"path":"reports/a.txt"}"""), null, default);
            doc.RootElement.GetProperty("content").GetString().Should().Be("hello");

            foreach (var escape in new[] { "../other/x.txt", "/etc/passwd", "reports/../../x" })
            {
                var act = () => read.ExecuteAsync(JsonDocument.Parse(JsonSerializer.Serialize(new { path = escape })), null, default);
                await act.Should().ThrowAsync<ConnectorOperationException>(escape);
            }

            // Another workspace cannot see the file.
            var other = new FileSystemReadHandler(config, new ConnectorCallContext { WorkspaceId = Guid.NewGuid() }, NullLogger<FileSystemReadHandler>.Instance);
            var cross = () => other.ExecuteAsync(JsonDocument.Parse("""{"path":"reports/a.txt"}"""), null, default);
            await cross.Should().ThrowAsync<ConnectorOperationException>();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystem_DisabledWithoutRootPath()
    {
        var read = new FileSystemReadHandler(Config(), new ConnectorCallContext { WorkspaceId = Guid.NewGuid() }, NullLogger<FileSystemReadHandler>.Instance);
        var act = () => read.ExecuteAsync(JsonDocument.Parse("""{"path":"a.txt"}"""), null, default);
        (await act.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("not enabled");
    }

    [Fact]
    public async Task ScriptShell_DisabledByDefault()
    {
        var handler = new ScriptShellRunHandler(Config(), new ConnectorCallContext { WorkspaceId = Guid.NewGuid() }, NullLogger<ScriptShellRunHandler>.Instance);
        var act = () => handler.ExecuteAsync(JsonDocument.Parse("""{"interpreter":"bash","script":"echo hi"}"""), null, default);
        (await act.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("disabled");
    }

    [Fact]
    public async Task ScriptShell_WhenEnabled_RunsWithArgsOnStdin_AndFailsOnNonZeroExit()
    {
        var handler = new ScriptShellRunHandler(Config((ScriptShellRunHandler.EnabledKey, "true")),
            new ConnectorCallContext { WorkspaceId = Guid.NewGuid() }, NullLogger<ScriptShellRunHandler>.Instance);

        var ok = await handler.ExecuteAsync(JsonDocument.Parse("""{"interpreter":"bash","script":"echo \"arg=$1\"","args":["a; rm -rf /"]}"""), null, default);
        ok.RootElement.GetProperty("stdout").GetString()!.Trim().Should().Be("arg=a; rm -rf /"); // passed as data, not executed

        var fail = () => handler.ExecuteAsync(JsonDocument.Parse("""{"interpreter":"bash","script":"echo nope >&2; exit 3"}"""), null, default);
        (await fail.Should().ThrowAsync<ConnectorOperationException>()).Which.Message.Should().Contain("code 3").And.Contain("nope");

        var badInterpreter = () => handler.ExecuteAsync(JsonDocument.Parse("""{"interpreter":"sh -c","script":"id"}"""), null, default);
        await badInterpreter.Should().ThrowAsync<ConnectorOperationException>();
    }

    [Fact]
    public async Task HttpRest_AppliesCredentialTypes()
    {
        HttpRequestMessage? sent = null;
        var http = new Mock<HttpMessageHandler>();
        http.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":1}""") });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(http.Object));
        var handler = new HttpGetHandler(factory.Object, NullLogger<HttpGetHandler>.Instance);
        var input = JsonDocument.Parse("""{"url":"https://api.example.com/x","api_key_header":"X-Token"}""");

        await handler.ExecuteAsync(input, new ConnectorCredential(Guid.NewGuid(), "k", "ApiKey", "key-1"), default);
        sent!.Headers.GetValues("X-Token").Single().Should().Be("key-1");

        await handler.ExecuteAsync(input, new ConnectorCredential(Guid.NewGuid(), "b", "BearerToken", "tok"), default);
        sent!.Headers.Authorization!.ToString().Should().Be("Bearer tok");

        await handler.ExecuteAsync(input, new ConnectorCredential(Guid.NewGuid(), "u", "BasicAuth", "user:pw"), default);
        sent!.Headers.Authorization!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public void ConnectorCredential_ToString_NeverContainsTheSecret()
    {
        var cred = new ConnectorCredential(Guid.NewGuid(), "prod-db", "ConnectionString", "Password=hunter2");
        cred.ToString().Should().NotContain("hunter2").And.Contain("***");
    }
}
