using System.Net;
using System.Text.Json;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Notifications;
using Xunit;

namespace VotschVc3.Core.Tests;

public sealed class BrevoEmailSenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoAttachmentsOmitsOptionalField(bool emptyList)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        await Sender(client).SendAsync(new EmailMessage("recipient@example.com", "Result", "Completed",
            Attachments: emptyList ? [] : null));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.False(json.RootElement.TryGetProperty("attachment", out _));
        Assert.False(json.RootElement.TryGetProperty("htmlContent", out _));
        Assert.Equal("Completed", json.RootElement.GetProperty("textContent").GetString());
    }

    [Fact]
    public async Task CalibrationCompletionWithoutAttachmentsProducesValidRequest()
    {
        var completion = CalibrationCompletionEmail.Create(new CalibrationRunRecord(), null);
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        await Sender(client).SendAsync(new EmailMessage("recipient@example.com", completion.Subject,
            completion.Text, completion.Html, completion.Attachments));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.False(json.RootElement.TryGetProperty("attachment", out _));
        Assert.Equal(completion.Html, json.RootElement.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task RealAttachmentRetainsNameAndBase64Content()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        await Sender(client).SendAsync(new EmailMessage("recipient@example.com", "Result", "Completed",
            Attachments: [new EmailAttachment("result.txt", [1, 2, 3], "text/plain")]));
        using var json = JsonDocument.Parse(handler.Body!);
        var attachments = json.RootElement.GetProperty("attachment");
        Assert.Equal(1, attachments.GetArrayLength());
        Assert.Equal("result.txt", attachments[0].GetProperty("name").GetString());
        Assert.Equal("AQID", attachments[0].GetProperty("content").GetString());
    }

    private static BrevoEmailSender Sender(HttpClient client) => new(new EmailSettings
    {
        From = "sender@example.com",
        HttpApiKey = "test-key",
    }, client);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
