using Emplyo.Web.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Emplyo.UnitTests;

public class ImageUploadsTests
{
    [Fact]
    public async Task A_jpeg_with_a_jpeg_extension_is_accepted()
    {
        var file = File("photo.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.Null(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task A_png_with_a_png_extension_is_accepted()
    {
        var file = File("photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 });

        Assert.Null(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task A_webp_with_a_webp_extension_is_accepted()
    {
        var file = File("photo.webp", new byte[]
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P'
        });

        Assert.Null(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task Html_disguised_as_an_image_is_rejected()
    {
        var file = File("photo.jpg", System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));

        Assert.NotNull(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task An_extension_and_image_signature_mismatch_is_rejected()
    {
        var file = File("photo.png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.NotNull(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task Disallowed_extensions_are_rejected()
    {
        var file = File("photo.svg", new byte[] { 0x3C, 0x73, 0x76, 0x67, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.NotNull(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task Oversized_images_are_rejected_without_buffering_them()
    {
        var file = new FormFile(new MemoryStream(new byte[1]), 0, 4 * 1024 * 1024 + 1, "photo", "photo.jpg");

        Assert.NotNull(await ImageUploads.ValidateAsync(file));
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        Assert.NotNull(await ImageUploads.ValidateAsync(null));
        Assert.NotNull(await ImageUploads.ValidateAsync(File("photo.jpg", Array.Empty<byte>())));
    }

    private static FormFile File(string name, byte[] content)
        => new(new MemoryStream(content), 0, content.Length, "photo", name);
}
