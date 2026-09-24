using Emplyo.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Emplyo.Infrastructure.Services;

public class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@emplyo.local";
    public string FromName { get; set; } = "Emplyo";
}

public class LoggingEmailSender : IAppEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(EmailOptions options, ILogger<LoggingEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Email delivery is not configured. Suppressed notification email (subject: {Subject}) for recipient.",
                subject);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("Email is enabled but no SMTP host is configured. Email not sent.");
            return Task.CompletedTask;
        }

        using var message = new System.Net.Mail.MailMessage(_options.FromAddress, toEmail, subject, htmlBody)
        {
            IsBodyHtml = true
        };

        using var client = new System.Net.Mail.SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl
        };

        if (!string.IsNullOrEmpty(_options.UserName))
            client.Credentials = new System.Net.NetworkCredential(_options.UserName, _options.Password);

        try
        {
            client.Send(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to recipient.");
        }

        return Task.CompletedTask;
    }
}
