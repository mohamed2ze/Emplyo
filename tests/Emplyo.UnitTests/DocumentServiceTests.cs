using System.Text;
using Emplyo.Application.Documents;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class DocumentServiceTests
{
    private static DocumentService Create(AppDbContext db, FakeFileStorage storage, FakeAudit? audit = null)
        => new(db, storage, audit ?? new FakeAudit(), new FakeClock());

    private static DocumentUploadRequest ValidRequest(int employeeId, string fileName = "contract.pdf", string contentType = "application/pdf")
        => new()
        {
            EmployeeId = employeeId,
            Title = "Employment contract",
            Category = DocumentCategory.Contract,
            OriginalFileName = fileName,
            ContentType = contentType,
            SizeBytes = 3,
            Content = new MemoryStream(Encoding.UTF8.GetBytes("abc"))
        };

    [Fact]
    public async Task Upload_rejects_a_missing_employee()
    {
        using var db = TestDb.Create();
        var service = Create(db, new FakeFileStorage());

        await Assert.ThrowsAsync<NotFoundException>(() => service.UploadAsync(ValidRequest(999), "u1", "User"));
    }

    [Fact]
    public async Task Upload_requires_a_title()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var request = ValidRequest(employee.Id);
        request.Title = "   ";

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(db, new FakeFileStorage()).UploadAsync(request, "u1", "User"));
    }

    [Fact]
    public async Task Upload_rejects_an_empty_file()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var request = ValidRequest(employee.Id);
        request.SizeBytes = 0;

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(db, new FakeFileStorage()).UploadAsync(request, "u1", "User"));
    }

    [Fact]
    public async Task Upload_rejects_a_file_larger_than_ten_megabytes()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var request = ValidRequest(employee.Id);
        request.SizeBytes = 10 * 1024 * 1024 + 1;

        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(db, new FakeFileStorage()).UploadAsync(request, "u1", "User"));
    }

    [Theory]
    [InlineData("payload.exe", "application/octet-stream")]
    [InlineData("README", "text/plain")]
    public async Task Upload_rejects_disallowed_file_extensions(string fileName, string contentType)
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(db, new FakeFileStorage()).UploadAsync(ValidRequest(employee.Id, fileName, contentType), "u1", "User"));
    }

    [Fact]
    public async Task Upload_rejects_a_disallowed_content_type()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(db, new FakeFileStorage()).UploadAsync(ValidRequest(employee.Id, "payload.pdf", "application/x-msdownload"), "u1", "User"));
    }

    [Fact]
    public async Task Upload_uses_the_storage_name_and_strips_paths_from_the_original_name()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var storage = new FakeFileStorage();
        var audit = new FakeAudit();
        var request = ValidRequest(employee.Id, "../../outside.PDF");
        request.Title = "  Contract  ";

        var id = await Create(db, storage, audit).UploadAsync(request, "uploader", "HR User");

        var document = await db.EmployeeDocuments.FindAsync(id);
        Assert.NotNull(document);
        Assert.Equal("Contract", document!.Title);
        Assert.Equal("outside.PDF", document.OriginalFileName);
        Assert.Equal("stored-1.pdf", document.StoredFileName);
        Assert.Equal("uploader", document.UploadedByUserId);
        Assert.Equal("HR User", document.UploadedByName);
        Assert.Contains("Document.Uploaded", audit.Recorded);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task Upload_defaults_a_blank_content_type_to_octet_stream()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);

        var id = await Create(db, new FakeFileStorage()).UploadAsync(
            ValidRequest(employee.Id, "notes.txt", contentType: ""), "u1", "User");

        Assert.Equal("application/octet-stream", (await db.EmployeeDocuments.FindAsync(id))!.ContentType);
    }

    [Fact]
    public async Task Listing_hides_confidential_documents_from_regular_employees()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        db.EmployeeDocuments.AddRange(
            NewDocument(employee.Id, "Public policy", confidential: false),
            NewDocument(employee.Id, "Salary review", confidential: true));
        await db.SaveChangesAsync();
        var service = Create(db, new FakeFileStorage());

        var visible = await service.GetForEmployeeAsync(employee.Id, includeConfidential: false);

        Assert.Equal("Public policy", Assert.Single(visible).Title);
    }

    [Fact]
    public async Task Listing_includes_confidential_documents_for_privileged_users()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        db.EmployeeDocuments.AddRange(
            NewDocument(employee.Id, "Public policy", confidential: false),
            NewDocument(employee.Id, "Salary review", confidential: true));
        await db.SaveChangesAsync();
        var service = Create(db, new FakeFileStorage());

        var visible = await service.GetForEmployeeAsync(employee.Id, includeConfidential: true);

        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public async Task Download_lookup_returns_the_stored_file_information()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var document = NewDocument(employee.Id, "Public policy", confidential: false);
        db.EmployeeDocuments.Add(document);
        await db.SaveChangesAsync();
        var service = Create(db, new FakeFileStorage());

        var download = await service.GetForDownloadAsync(document.Id);

        Assert.NotNull(download);
        Assert.Equal(document.StoredFileName, download!.StoredFileName);
        Assert.Equal(document.EmployeeId, download.EmployeeId);
    }

    [Fact]
    public async Task Delete_removes_the_record_and_the_stored_file()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var storage = new FakeFileStorage();
        storage.Files["stored-file.pdf"] = new byte[] { 1 };
        var document = NewDocument(employee.Id, "To delete", confidential: false);
        document.StoredFileName = "stored-file.pdf";
        db.EmployeeDocuments.Add(document);
        await db.SaveChangesAsync();

        await Create(db, storage).DeleteAsync(document.Id);

        Assert.Null(await db.EmployeeDocuments.FindAsync(document.Id));
        Assert.Contains("stored-file.pdf", storage.Deleted);
        Assert.DoesNotContain("stored-file.pdf", storage.Files.Keys);
    }

    [Fact]
    public async Task Delete_still_removes_the_record_when_storage_cleanup_fails()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var storage = new FakeFileStorage { ThrowOnDelete = true };
        var document = NewDocument(employee.Id, "To delete", confidential: false);
        db.EmployeeDocuments.Add(document);
        await db.SaveChangesAsync();

        await Create(db, storage).DeleteAsync(document.Id);

        Assert.Null(await db.EmployeeDocuments.FindAsync(document.Id));
    }

    private static EmployeeDocument NewDocument(int employeeId, string title, bool confidential) => new()
    {
        EmployeeId = employeeId,
        Title = title,
        Category = DocumentCategory.Other,
        StoredFileName = $"{Guid.NewGuid():N}.txt",
        OriginalFileName = "document.txt",
        ContentType = "text/plain",
        SizeBytes = 10,
        IsConfidential = confidential,
        UploadedByUserId = "u1",
        UploadedByName = "User",
        UploadedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
