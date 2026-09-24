using Emplyo.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Emplyo.Infrastructure.Services;

public class FileStorageOptions
{
    public string DocumentsRoot { get; set; } = "App_Data/documents";
}

public class FileStorageService : IFileStorageService
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(FileStorageOptions options, ILogger<FileStorageService> logger)
    {
        _options = options;
        _logger = logger;
        Directory.CreateDirectory(RootPath);
    }

    private string RootPath => Path.GetFullPath(_options.DocumentsRoot);

    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(originalFileName);
        if (extension.Length > 10) extension = extension[..10];

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(RootPath, storedName);

        // Defence in depth: ensure the resolved path stays inside the storage root.
        var resolved = Path.GetFullPath(fullPath);
        if (!resolved.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage path.");

        await using (var target = new FileStream(resolved, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(target, ct);
        }

        return storedName;
    }

    public Stream OpenRead(string storedFileName)
    {
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(RootPath, safeName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Stored document could not be found.", safeName);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Delete(string storedFileName)
    {
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(RootPath, safeName);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted stored document {Document}.", safeName);
        }
    }

    public bool Exists(string storedFileName)
        => File.Exists(Path.Combine(RootPath, Path.GetFileName(storedFileName)));
}
