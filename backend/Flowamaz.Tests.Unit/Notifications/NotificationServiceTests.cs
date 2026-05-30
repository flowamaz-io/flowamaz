using Flowamaz.Application.Notifications;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence;
using Flowamaz.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flowamaz.Tests.Unit.Notifications;

public class NotificationServiceTests
{
    private static FlowAmazDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowAmazDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid():N}")
            .Options);

    private static NotificationService NewService(FlowAmazDbContext db) =>
        new(new NotificationRepository(db), new EfUnitOfWork(db), NullLogger<NotificationService>.Instance);

    [Fact]
    public async Task NotifyInstanceComplete_creates_notification_for_creator()
    {
        var db = NewDb();
        var service = NewService(db);
        var creator = Guid.NewGuid();

        await service.NotifyInstanceCompleteAsync(creator, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Completed);

        var items = await service.GetForUserAsync(creator, unreadOnly: false, 20);
        items.Should().HaveCount(1);
        items[0].Type.Should().Be("instance_complete");
        items[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task NotifyGatePending_creates_notification_for_assignee()
    {
        var db = NewDb();
        var service = NewService(db);
        var assignee = Guid.NewGuid();

        await service.NotifyGatePendingAsync(assignee, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "gate-1");

        var items = await service.GetForUserAsync(assignee, unreadOnly: true, 20);
        items.Should().ContainSingle(n => n.Type == "gate_pending");
    }

    [Fact]
    public async Task MarkAllRead_marks_every_unread_notification()
    {
        var db = NewDb();
        var service = NewService(db);
        var user = Guid.NewGuid();
        await service.NotifyInstanceCompleteAsync(user, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Completed);
        await service.NotifyGatePendingAsync(user, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "g");

        (await service.GetUnreadCountAsync(user)).Should().Be(2);
        await service.MarkAllReadAsync(user);

        (await service.GetUnreadCountAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task NotifyGateDecided_creates_notification_for_requestor()
    {
        var db = NewDb();
        var service = NewService(db);
        var requestor = Guid.NewGuid();

        await service.NotifyGateDecidedAsync(requestor, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "approved");

        var items = await service.GetForUserAsync(requestor, unreadOnly: false, 20);
        items.Should().ContainSingle(n => n.Type == "gate_decided" && n.Title.Contains("approved"));
    }

    [Fact]
    public async Task NotifyMemberInvited_creates_notification_for_admin()
    {
        var db = NewDb();
        var service = NewService(db);
        var admin = Guid.NewGuid();

        await service.NotifyMemberInvitedAsync(admin, Guid.NewGuid(), Guid.NewGuid(), "newbie@acme.test");

        var items = await service.GetForUserAsync(admin, unreadOnly: false, 20);
        items.Should().ContainSingle(n => n.Type == "member_invited" && n.Message.Contains("newbie@acme.test"));
    }

    [Fact]
    public async Task MarkRead_marks_a_single_notification()
    {
        var db = NewDb();
        var service = NewService(db);
        var user = Guid.NewGuid();
        await service.NotifyInstanceCompleteAsync(user, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Failed);
        var id = (await service.GetForUserAsync(user, false, 20))[0].Id;

        await service.MarkReadAsync(id, user);

        (await service.GetUnreadCountAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task GetForUser_is_scoped_to_the_user()
    {
        var db = NewDb();
        var service = NewService(db);
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await service.NotifyInstanceCompleteAsync(mine, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Completed);
        await service.NotifyInstanceCompleteAsync(other, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstanceStatus.Completed);

        var items = await service.GetForUserAsync(mine, unreadOnly: false, 20);
        items.Should().HaveCount(1);
    }
}
