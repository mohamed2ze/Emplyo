using Emplyo.Application.Announcements;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class AnnouncementServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    private static AnnouncementService Create(AppDbContext db, FakeAudit? audit = null)
        => new(db, audit ?? new FakeAudit(), new FakeClock { UtcNow = Now });

    private static AnnouncementFormDto Form(
        string title = "Office update",
        string body = "The office is closed.",
        AnnouncementAudience audience = AnnouncementAudience.Everyone,
        bool publishNow = true,
        DateTime? publishAt = null,
        DateTime? expiresAt = null,
        int? departmentId = null) => new()
    {
        Title = title,
        Body = body,
        Audience = audience,
        PublishNow = publishNow,
        PublishAt = publishAt,
        ExpiresAt = expiresAt,
        DepartmentId = departmentId
    };

    private static Announcement NewAnnouncement(string title, AnnouncementAudience audience = AnnouncementAudience.Everyone,
        bool published = true, DateTime? publishAt = null, DateTime? expiresAt = null, int? departmentId = null,
        DateTime? createdAt = null) => new()
    {
        Title = title,
        Body = "Body",
        Audience = audience,
        IsPublished = published,
        PublishAtUtc = publishAt,
        ExpiresAtUtc = expiresAt,
        DepartmentId = departmentId,
        CreatedByUserId = "author",
        CreatedByName = "Author",
        CreatedAtUtc = createdAt ?? Now.AddDays(-1),
        UpdatedAtUtc = createdAt ?? Now.AddDays(-1)
    };

    [Fact]
    public async Task Create_publishes_trims_and_clears_an_irrelevant_department()
    {
        using var db = TestDb.Create();
        var audit = new FakeAudit();
        var service = Create(db, audit);

        var id = await service.CreateAsync("author-id", "Author", Form(
            title: "  Office update  ", body: "  The office is closed.  ", departmentId: 7));

        var announcement = await service.GetByIdAsync(id);
        Assert.NotNull(announcement);
        Assert.Equal("Office update", announcement!.Title);
        Assert.Equal("The office is closed.", announcement.Body);
        Assert.True(announcement.IsPublished);
        Assert.Equal(Now, announcement.PublishAtUtc);
        Assert.Null(announcement.DepartmentName);
        Assert.Contains("Announcement.Created", audit.Recorded);
    }

    [Theory]
    [InlineData("", "Body")]
    [InlineData("Title", "")]
    public async Task Create_rejects_missing_title_or_body(string title, string body)
    {
        using var db = TestDb.Create();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(db).CreateAsync("author-id", "Author", Form(title: title, body: body)));
    }

    [Fact]
    public async Task Create_rejects_an_expiration_before_the_publication()
    {
        using var db = TestDb.Create();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(db).CreateAsync("author-id", "Author",
            Form(publishNow: false, publishAt: Now.AddDays(2), expiresAt: Now.AddDays(1))));
    }

    [Fact]
    public async Task Create_requires_a_department_for_department_audience()
    {
        using var db = TestDb.Create();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(db).CreateAsync("author-id", "Author",
            Form(audience: AnnouncementAudience.Department)));
    }

    [Fact]
    public async Task Create_keeps_the_department_for_department_audience()
    {
        using var db = TestDb.Create();
        var department = await Seed.DepartmentAsync(db);
        var service = Create(db);

        var id = await service.CreateAsync("author-id", "Author", Form(
            audience: AnnouncementAudience.Department, departmentId: department.Id));

        var announcement = await service.GetByIdAsync(id);
        Assert.Equal("Engineering", announcement!.DepartmentName);
    }

    [Fact]
    public async Task Visibility_excludes_drafts_future_publications_and_expired_announcements()
    {
        using var db = TestDb.Create();
        db.Announcements.AddRange(
            NewAnnouncement("Active"),
            NewAnnouncement("Draft", published: false),
            NewAnnouncement("Scheduled", publishAt: Now.AddDays(1)),
            NewAnnouncement("Expired", publishAt: Now.AddDays(-2), expiresAt: Now.AddDays(-1)));
        await db.SaveChangesAsync();
        var service = Create(db);

        var visible = await service.GetActiveForRoleAsync(new[] { "Employee" }, null);

        Assert.Equal("Active", Assert.Single(visible).Title);
    }

    [Fact]
    public async Task Visibility_honors_audience_and_department_for_regular_employees()
    {
        using var db = TestDb.Create();
        db.Announcements.AddRange(
            NewAnnouncement("Everyone", AnnouncementAudience.Everyone),
            NewAnnouncement("Employees", AnnouncementAudience.Employees),
            NewAnnouncement("Admins", AnnouncementAudience.Administrators),
            NewAnnouncement("HR", AnnouncementAudience.HrManagers),
            NewAnnouncement("Own department", AnnouncementAudience.Department, departmentId: 10),
            NewAnnouncement("Other department", AnnouncementAudience.Department, departmentId: 20));
        await db.SaveChangesAsync();
        var service = Create(db);

        var visible = await service.GetActiveForRoleAsync(new[] { "Employee" }, departmentId: 10);

        Assert.Equal(new[] { "Employees", "Everyone", "Own department" }, visible.Select(a => a.Title).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Visibility_includes_hr_audience_for_hr_managers_but_not_administrator_audience()
    {
        using var db = TestDb.Create();
        db.Announcements.AddRange(
            NewAnnouncement("Everyone", AnnouncementAudience.Everyone),
            NewAnnouncement("Admins", AnnouncementAudience.Administrators),
            NewAnnouncement("HR", AnnouncementAudience.HrManagers));
        await db.SaveChangesAsync();
        var service = Create(db);

        var visible = await service.GetActiveForRoleAsync(new[] { "HRManager" }, null);

        Assert.Equal(new[] { "Everyone", "HR" }, visible.Select(a => a.Title).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Visibility_includes_both_staff_audiences_for_administrators()
    {
        using var db = TestDb.Create();
        db.Announcements.AddRange(
            NewAnnouncement("Everyone", AnnouncementAudience.Everyone),
            NewAnnouncement("Admins", AnnouncementAudience.Administrators),
            NewAnnouncement("HR", AnnouncementAudience.HrManagers));
        await db.SaveChangesAsync();
        var service = Create(db);

        var visible = await service.GetActiveForRoleAsync(new[] { "Administrator" }, null);

        Assert.Equal(new[] { "Admins", "Everyone", "HR" }, visible.Select(a => a.Title).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Visibility_treats_publication_and_expiration_boundaries_as_inclusive()
    {
        using var db = TestDb.Create();
        db.Announcements.AddRange(
            NewAnnouncement("Starts now", publishAt: Now),
            NewAnnouncement("Ends now", publishAt: Now.AddDays(-1), expiresAt: Now));
        await db.SaveChangesAsync();
        var service = Create(db);

        var visible = await service.GetActiveForRoleAsync(new[] { "Employee" }, null);

        Assert.Equal(new[] { "Ends now", "Starts now" }, visible.Select(a => a.Title).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Visible_feed_orders_newest_first_and_honors_the_take_limit()
    {
        using var db = TestDb.Create();
        db.Announcements.AddRange(
            NewAnnouncement("Oldest", publishAt: Now.AddDays(-3)),
            NewAnnouncement("Newest", publishAt: Now.AddDays(-1)),
            NewAnnouncement("Middle", publishAt: Now.AddDays(-2)));
        await db.SaveChangesAsync();
        var service = Create(db);

        var visible = await service.GetVisibleForUserAsync(new[] { "Employee" }, null, take: 2);

        Assert.Equal(new[] { "Newest", "Middle" }, visible.Select(a => a.Title));
    }

    [Fact]
    public async Task Publishing_sets_the_publication_time_only_when_missing()
    {
        using var db = TestDb.Create();
        var audit = new FakeAudit();
        var existingDate = Now.AddDays(-5);
        var scheduled = NewAnnouncement("Scheduled", published: false, publishAt: existingDate);
        var unscheduled = NewAnnouncement("Unscheduled", published: false);
        db.Announcements.AddRange(scheduled, unscheduled);
        await db.SaveChangesAsync();
        var service = Create(db, audit);

        await service.SetPublishedAsync(scheduled.Id, true);
        await service.SetPublishedAsync(unscheduled.Id, true);

        Assert.Equal(existingDate, (await db.Announcements.FindAsync(scheduled.Id))!.PublishAtUtc);
        Assert.Equal(Now, (await db.Announcements.FindAsync(unscheduled.Id))!.PublishAtUtc);
        Assert.Contains("Announcement.Published", audit.Recorded);
    }

    [Fact]
    public async Task Archiving_marks_the_announcement_unpublished()
    {
        using var db = TestDb.Create();
        var announcement = NewAnnouncement("Active");
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync();
        var audit = new FakeAudit();
        var service = Create(db, audit);

        await service.SetPublishedAsync(announcement.Id, false);

        Assert.False((await db.Announcements.FindAsync(announcement.Id))!.IsPublished);
        Assert.Contains("Announcement.Archived", audit.Recorded);
    }

    [Fact]
    public async Task Delete_removes_the_announcement()
    {
        using var db = TestDb.Create();
        var announcement = NewAnnouncement("To delete");
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync();
        var service = Create(db);

        await service.DeleteAsync(announcement.Id);

        Assert.Null(await service.GetByIdAsync(announcement.Id));
    }
}
