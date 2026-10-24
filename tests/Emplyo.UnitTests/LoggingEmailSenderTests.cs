using System.Net;
using System.Net.Sockets;
using System.Text;
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

    [Fact]
    public async Task Enabled_delivery_sends_the_message_to_the_smtp_server()
    {
        await using var sink = new SmtpSink();
        var sender = Create(new EmailOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = sink.Port,
            UseSsl = false,
            FromAddress = "sender@test.local",
            FromName = "Emplyo"
        });

        await sender.SendAsync("recipient@test.local", "Password reset", "<p>Reset link</p>");

        var message = await sink.Message.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Subject: Password reset", message);
        Assert.Contains("recipient@test.local", message);
        Assert.Contains("<p>Reset link</p>", message);
    }

    private static LoggingEmailSender Create(EmailOptions options)
        => new(options, NullLogger<LoggingEmailSender>.Instance);

    private sealed class SmtpSink : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _serverTask;

        public SmtpSink()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = Task.Run(ServeAsync);
        }

        public int Port { get; }
        public TaskCompletionSource<string> Message { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\r\n"
                };

                await writer.WriteLineAsync("220 localhost ESMTP");
                var data = new StringBuilder();
                var receivingData = false;

                while (true)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token);
                    if (line is null) break;

                    if (receivingData)
                    {
                        if (line == ".")
                        {
                            receivingData = false;
                            Message.TrySetResult(data.ToString());
                            data.Clear();
                            await writer.WriteLineAsync("250 OK");
                            continue;
                        }

                        data.AppendLine(line);
                        continue;
                    }

                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        receivingData = true;
                        await writer.WriteLineAsync("354 End data");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 Bye");
                        break;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            _cancellation.Dispose();
        }
    }
}
