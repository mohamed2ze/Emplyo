using System.Text;
using Emplyo.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Emplyo.UnitTests;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileStorageService _storage;

    public FileStorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"emplyo-storage-{Guid.NewGuid():N}");
        _storage = new FileStorageService(
            new FileStorageOptions { DocumentsRoot = _root },
            NullLogger<FileStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Save_generates_a_stored_name_and_preserves_a_lowercase_extension()
    {
        var name = await _storage.SaveAsync(Content("hello"), "Report.PDF");

        Assert.EndsWith(".pdf", name);
        Assert.NotEqual("Report.PDF", name);
        Assert.True(_storage.Exists(name));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Save_and_open_read_round_trip_the_content()
    {
        var name = await _storage.SaveAsync(Content("hello world"), "note.txt");

        using var stream = _storage.OpenRead(name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal("hello world", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Saved_files_ignore_paths_supplied_in_the_original_file_name()
    {
        var name = await _storage.SaveAsync(Content("payload"), "../../outside.txt");

        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("../../evil.txt")]
    [InlineData("..\\..\\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("nested/../../../evil.pdf")]
    [InlineData("..%2f..%2fevil.txt")]
    public async Task Malicious_original_file_names_cannot_escape_the_storage_root(string originalFileName)
    {
        var name = await _storage.SaveAsync(Content("payload"), originalFileName);

        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, name);
        var files = Directory.GetFiles(_root);
        Assert.Single(files);
        Assert.StartsWith(_root, files[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_generates_a_distinct_name_for_each_upload()
    {
        var first = await _storage.SaveAsync(Content("one"), "note.txt");
        var second = await _storage.SaveAsync(Content("two"), "note.txt");

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public async Task Exists_uses_only_the_file_name()
    {
        var name = await _storage.SaveAsync(Content("hello"), "note.txt");

        Assert.True(_storage.Exists(name));
        Assert.True(_storage.Exists(Path.Combine("nested", name)));
        Assert.False(_storage.Exists($"{name}.missing"));
    }

    [Fact]
    public void Open_read_reports_a_missing_file()
    {
        Assert.Throws<FileNotFoundException>(() => _storage.OpenRead("missing.txt"));
    }

    [Fact]
    public async Task Delete_removes_the_file_and_is_idempotent()
    {
        var name = await _storage.SaveAsync(Content("hello"), "note.txt");

        _storage.Delete(name);
        _storage.Delete(name);

        Assert.False(_storage.Exists(name));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Delete_uses_only_the_file_name()
    {
        var name = await _storage.SaveAsync(Content("hello"), "note.txt");

        _storage.Delete(Path.Combine("nested", name));

        Assert.False(_storage.Exists(name));
    }

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));
}
