using Emplyo.Application.Notifications;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class NotificationServiceTests
{
    private static NotificationService Create(AppDbContext db, FakeIdentityService? identity = null)
        => new(db, identity ?? new FakeIdentityService(), new FakeClock());

    [Fact]
    public async Task Notify_user_persists_the_notification()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        await service.NotifyUserAsync("user-1", "Leave approved", "Your request was approved.",
            "/Leave/Details/5", NotificationType.LeaveApproved);

        var notification = Assert.Single(db.Notifications);
        Assert.Equal("user-1", notification.UserId);
        Assert.Equal("/Leave/Details/5", notification.Url);
        Assert.Equal(NotificationType.LeaveApproved, notification.Type);
        Assert.False(notification.IsRead);
        Assert.Equal(new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), notification.CreatedAtUtc);
    }

    [Fact]
    public async Task Notify_user_without_an_id_is_a_no_op()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        await service.NotifyUserAsync("", "Title", "Message");

        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Notify_role_fans_out_to_active_users_in_the_role()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();
        identity.AddUser("hr-1", "hr1@test.local", "HR One", isActive: true, "HRManager");
        identity.AddUser("hr-2", "hr2@test.local", "HR Two", isActive: true, "HRManager");
        identity.AddUser("hr-3", "hr3@test.local", "HR Inactive", isActive: false, "HRManager");
        identity.AddUser("emp-1", "emp@test.local", "Employee", isActive: true, "Employee");
        var service = Create(db, identity);

        await service.NotifyRoleAsync("HRManager", "New leave request", "Awaiting review", "/Leave");

        Assert.Equal(2, db.Notifications.Count());
        Assert.All(db.Notifications, n => Assert.Contains(n.UserId, new[] { "hr-1", "hr-2" }));
    }

    [Fact]
    public async Task Notify_role_without_recipients_is_a_no_op()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        await service.NotifyRoleAsync("Administrator", "Title", "Message");

        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task The_inbox_is_filtered_to_the_user_and_ordered_newest_first()
    {
        using var db = TestDb.Create();
        db.Notifications.AddRange(
            NewNotification("user-1", "Old", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc)),
            NewNotification("user-1", "New", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)),
            NewNotification("user-2", "Other", new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var service = Create(db);

        var inbox = await service.GetForUserAsync("user-1");

        Assert.Equal(new[] { "New", "Old" }, inbox.Select(n => n.Title));
    }

    [Fact]
    public async Task The_inbox_can_return_only_unread_items_and_respect_the_take_limit()
    {
        using var db = TestDb.Create();
        var read = NewNotification("user-1", "Read", new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc));
        read.IsRead = true;
        db.Notifications.AddRange(
            read,
            NewNotification("user-1", "Unread old", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc)),
            NewNotification("user-1", "Unread new", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var service = Create(db);

        var unread = await service.GetForUserAsync("user-1", unreadOnly: true, take: 1);

        Assert.Equal("Unread new", Assert.Single(unread).Title);
    }

    [Fact]
    public async Task Mark_read_only_affects_the_owner()
    {
        using var db = TestDb.Create();
        var notification = NewNotification("user-2", "Private", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        var service = Create(db);

        var marked = await service.MarkReadAsync(notification.Id, "user-1");

        Assert.False(marked);
        Assert.False((await db.Notifications.FindAsync(notification.Id))!.IsRead);
    }

    [Fact]
    public async Task Mark_read_marks_an_owned_notification()
    {
        using var db = TestDb.Create();
        var notification = NewNotification("user-1", "Mine", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        var service = Create(db);

        var marked = await service.MarkReadAsync(notification.Id, "user-1");

        Assert.True(marked);
        Assert.True((await db.Notifications.FindAsync(notification.Id))!.IsRead);
    }

    [Fact]
    public async Task Mark_all_read_only_affects_the_user()
    {
        using var db = TestDb.Create();
        db.Notifications.AddRange(
            NewNotification("user-1", "Mine 1", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc)),
            NewNotification("user-1", "Mine 2", new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc)),
            NewNotification("user-2", "Other", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var service = Create(db);

        await service.MarkAllReadAsync("user-1");

        Assert.All(db.Notifications.Where(n => n.UserId == "user-1"), n => Assert.True(n.IsRead));
        Assert.False(db.Notifications.Single(n => n.UserId == "user-2").IsRead);
    }

    [Fact]
    public async Task The_unread_count_is_scoped_to_the_user()
    {
        using var db = TestDb.Create();
        var read = NewNotification("user-1", "Read", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        read.IsRead = true;
        db.Notifications.AddRange(
            read,
            NewNotification("user-1", "Unread", new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc)),
            NewNotification("user-2", "Other", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var service = Create(db);

        Assert.Equal(1, await service.GetUnreadCountAsync("user-1"));
    }

    private static Notification NewNotification(string userId, string title, DateTime createdAt) => new()
    {
        UserId = userId,
        Title = title,
        Message = "Message",
        CreatedAtUtc = createdAt
    };
}
