using System.Net.Http.Headers;
using System.Net.Http.Json;
using Clinco.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clinco.Infrastructure.Sms;

internal sealed class HttpSmsGateway(
    HttpClient httpClient,
    IOptions<SmsOptions> options,
    ILogger<HttpSmsGateway> logger) : ISmsGateway
{
    private readonly SmsOptions _options = options.Value;

    public async Task<SmsSendResult> SendAsync(string phoneNumber, string messageContent, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "SMS provider disabled. Simulated send to {PhoneNumber})",
                phoneNumber);

            return new SmsSendResult(true, $"mock-{Guid.NewGuid():N}", null);
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return new SmsSendResult(false, null, "SMS BaseUrl is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + "/send")
            {
                Content = JsonContent.Create(new
                {
                    to = phoneNumber,
                    message = messageContent,
                    from = _options.SenderName
                })
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new SmsSendResult(false, null, $"HTTP {(int)response.StatusCode}: {error}");
            }

            var providerMessageId = response.Headers.TryGetValues("x-message-id", out var values)
                ? values.FirstOrDefault()
                : null;

            return new SmsSendResult(true, providerMessageId ?? $"provider-{Guid.NewGuid():N}", null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", phoneNumber);
            return new SmsSendResult(false, null, ex.Message);
        }
    }
}
