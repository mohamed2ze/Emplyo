namespace Emplyo.Web.Services;

public static class ImageUploads
{
    private const long MaxBytes = 4 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    public static async Task<string?> ValidateAsync(IFormFile? file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return "Select an image file.";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || file.Length > MaxBytes)
            return "Profile photo must be a JPG, PNG or WEBP image under 4 MB.";

        var expectedContentType = extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        var header = new byte[12];
        await using (var stream = file.OpenReadStream())
        {
            var totalRead = 0;
            while (totalRead < header.Length)
            {
                var read = await stream.ReadAsync(header.AsMemory(totalRead, header.Length - totalRead), ct);
                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead < header.Length)
                return "The uploaded file is not a valid image.";
        }

        return DetectContentType(header) == expectedContentType
            ? null
            : "The uploaded file is not a valid image.";
    }

    private static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";

        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return "image/png";

        if (header.Length >= 12 &&
            header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
            header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
            return "image/webp";

        return null;
    }
}
