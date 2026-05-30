using FluentAssertions;
using Flowamaz.Core.Enums;
using Flowamaz.Core.Interfaces.Services;
using Flowamaz.Tests.Integration.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Flowamaz.Tests.Integration.Phase6;

/// <summary>
/// Notification centre over the real DB: instance-complete notifies the creator, gate-pending
/// notifies the assignee, and mark-all-read clears the unread count.
/// </summary>
[Collection("api")]
public class Phase6NotificationTests : ApiTestBase
{
    public Phase6NotificationTests(IntegrationApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Instance_complete_creates_a_notification_for_the_creator()
    {
        await Fixture.ResetRedisAsync();
        using var scope = Fixture.Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var creator = Guid.NewGuid();

        await notifications.NotifyInstanceCompleteAsync(creator, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Completed);

        var items = await notifications.GetForUserAsync(creator, unreadOnly: false, 20);
        items.Should().ContainSingle(n => n.Type == "instance_complete");
    }

    [Fact]
    public async Task Gate_pending_creates_a_notification_for_the_assignee()
    {
        using var scope = Fixture.Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var assignee = Guid.NewGuid();

        await notifications.NotifyGatePendingAsync(assignee, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "gate-1");

        (await notifications.GetForUserAsync(assignee, unreadOnly: true, 20))
            .Should().ContainSingle(n => n.Type == "gate_pending");
    }

    [Fact]
    public async Task Mark_all_read_zeroes_the_unread_count()
    {
        using var scope = Fixture.Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var user = Guid.NewGuid();
        await notifications.NotifyInstanceCompleteAsync(user, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Completed);
        await notifications.NotifyGatePendingAsync(user, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "g");

        (await notifications.GetUnreadCountAsync(user)).Should().Be(2);
        await notifications.MarkAllReadAsync(user);

        (await notifications.GetUnreadCountAsync(user)).Should().Be(0);
    }
}
