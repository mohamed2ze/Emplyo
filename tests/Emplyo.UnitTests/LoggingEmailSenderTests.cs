using Emplyo.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Emplyo.UnitTests;

public class LoggingEmailSenderTests
{
    [Fact]
    public void Disabled_email_delivery_reports_disabled()
    {
        var sender = Create(new EmailOptions { Enabled = false });

        Assert.False(sender.IsEnabled);
    }

    [Fact]
    public async Task Disabled_email_delivery_is_suppressed_without_throwing()
    {
        var sender = Create(new EmailOptions { Enabled = false, Host = "127.0.0.1", Port = 1 });

        await sender.SendAsync("recipient@test.local", "Subject", "<p>Body</p>");
    }

    [Fact]
    public async Task Enabled_delivery_without_a_host_is_suppressed_without_throwing()
    {
        var sender = Create(new EmailOptions { Enabled = true, Host = "" });

        Assert.True(sender.IsEnabled);
        await sender.SendAsync("recipient@test.local", "Subject", "<p>Body</p>");
    }

    [Fact]
    public async Task Delivery_failures_are_contained()
    {
        var sender = Create(new EmailOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = 1,
            UseSsl = false,
            FromAddress = "sender@test.local"
        });

        await sender.SendAsync("recipient@test.local", "Subject", "<p>Body</p>");
    }

    private static LoggingEmailSender Create(EmailOptions options)
        => new(options, NullLogger<LoggingEmailSender>.Instance);
}
