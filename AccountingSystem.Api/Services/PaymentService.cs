using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AccountingSystem.API.Services
{
    public class PaymentService : IPaymentService
    {
        private const int DefaultWebhookReplayWindowSeconds = 300;

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(IConfiguration configuration, ILogger<PaymentService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger ?? NullLogger<PaymentService>.Instance;
            _httpClient = new HttpClient();

            _httpClient.BaseAddress = new Uri("https://api.paymongo.com/v1/");

            var secretKey = _configuration["PayMongo:SecretKey"];
            if (AccountingSystem.API.Configuration.StartupConfigurationValidator.IsMissingOrPlaceholder(secretKey))
            {
                throw new InvalidOperationException(
                    AccountingSystem.API.Configuration.StartupConfigurationValidator.BuildMissingValueMessage("PayMongo:SecretKey"));
            }

            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(secretKey + ":"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
        }

        public async Task<PaymentSourceResponseDTO> CreatePaymentSourceAsync(CreateSourceDTO dto)
        {
            var request = new
            {
                data = new
                {
                    attributes = new
                    {
                        amount = (int)(dto.Amount * 100),
                        type = "gcash",
                        currency = "PHP",
                        redirect = new
                        {
                            success = dto.SuccessUrl ?? "http://localhost:5240/payment-callback",
                            failed = dto.FailedUrl ?? "http://localhost:5240/payment-callback"
                        },
                        billing = new { name = "System User", email = "user@example.com" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("sources", content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"PayMongo API Error: {responseString}");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<PayMongoSourceResponse>(responseString, options);

            if (result?.Data == null)
            {
                throw new InvalidOperationException("Invalid response from PayMongo API: missing data");
            }

            return new PaymentSourceResponseDTO
            {
                SourceId = result.Data.Id ?? throw new InvalidOperationException("PayMongo API returned null source ID"),
                CheckoutUrl = result.Data.Attributes?.Redirect?.CheckoutUrl ?? throw new InvalidOperationException("PayMongo API returned null checkout URL")
            };
        }

        public async Task<string> CreatePaymentSourceAsync(decimal amount, string description, string remarks)
        {
            var result = await CreatePaymentSourceAsync(new CreateSourceDTO { Amount = amount, Description = description, Remarks = remarks });
            return result.CheckoutUrl;
        }

        public async Task<bool> CapturePaymentAsync(string sourceId, decimal amount, string description)
        {
            var request = new
            {
                data = new
                {
                    attributes = new
                    {
                        amount = (int)(amount * 100),
                        source = new { id = sourceId, type = "source" },
                        currency = "PHP",
                        description = description
                    }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("payments", content);

            // Logging failure if capture fails could be helpful
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"PayMongo Capture Failed: {error}");
            }

            return response.IsSuccessStatusCode;
        }

        public bool VerifyWebhookSignature(string signature, string payload)
        {
            if (string.IsNullOrWhiteSpace(signature))
            {
                _logger.LogWarning("Rejected PayMongo webhook request: missing signature header.");
                return false;
            }

            var webhookSecret = _configuration["PayMongo:WebhookSecret"];
            if (AccountingSystem.API.Configuration.StartupConfigurationValidator.IsMissingOrPlaceholder(webhookSecret))
            {
                _logger.LogWarning("Rejected PayMongo webhook request: webhook secret is not configured.");
                return false;
            }

            var validatedWebhookSecret = webhookSecret!;

            if (!TryParseSignatureHeader(signature, out var timestamp, out var testSignature, out var liveSignature))
            {
                _logger.LogWarning("Rejected PayMongo webhook request: invalid signature header format.");
                return false;
            }

            var replayWindowSeconds = GetReplayWindowSeconds();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - timestamp) > replayWindowSeconds)
            {
                _logger.LogWarning(
                    "Rejected PayMongo webhook request: timestamp is outside replay window. Timestamp={Timestamp}, ReplayWindowSeconds={ReplayWindowSeconds}.",
                    timestamp,
                    replayWindowSeconds);
                return false;
            }

            var signedPayload = $"{timestamp}.{payload}";
            byte[] computedHash;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(validatedWebhookSecret)))
            {
                computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            }

            if (IsMatchingSignature(testSignature, computedHash) || IsMatchingSignature(liveSignature, computedHash))
            {
                return true;
            }

            _logger.LogWarning("Rejected PayMongo webhook request: signature mismatch.");
            return false;
        }

        private int GetReplayWindowSeconds()
        {
            var configuredReplayWindow = _configuration.GetValue<int?>("PayMongo:WebhookReplayWindowSeconds");
            return configuredReplayWindow is > 0
                ? configuredReplayWindow.Value
                : DefaultWebhookReplayWindowSeconds;
        }

        private static bool TryParseSignatureHeader(
            string signatureHeader,
            out long timestamp,
            out string testSignature,
            out string liveSignature)
        {
            timestamp = 0;
            testSignature = string.Empty;
            liveSignature = string.Empty;

            var parts = signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in parts)
            {
                var separatorIndex = part.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = part[..separatorIndex].Trim();
                var value = part[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!values.ContainsKey(key))
                {
                    values[key] = value;
                }
            }

            if (!values.TryGetValue("t", out var timestampValue) || !long.TryParse(timestampValue, out timestamp))
            {
                return false;
            }

            if (values.TryGetValue("te", out var parsedTestSignature) && !string.IsNullOrWhiteSpace(parsedTestSignature))
            {
                testSignature = parsedTestSignature;
            }

            if (values.TryGetValue("li", out var parsedLiveSignature) && !string.IsNullOrWhiteSpace(parsedLiveSignature))
            {
                liveSignature = parsedLiveSignature;
            }

            return true;
        }

        private static bool IsMatchingSignature(string? providedSignature, byte[] computedHash)
        {
            if (string.IsNullOrWhiteSpace(providedSignature))
            {
                return false;
            }

            byte[] providedHash;
            try
            {
                providedHash = Convert.FromHexString(providedSignature);
            }
            catch (FormatException)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(providedHash, computedHash);
        }
    }
}
