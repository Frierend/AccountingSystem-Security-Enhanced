using AccountingSystem.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AccountingSystem.Shared.DTOs
{
    // --- INTERNAL PAYMENT RECORDING ---
    public class RecordPaymentDTO
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid payment reference is required.")]
        public int ReferenceId { get; set; } // InvoiceId or BillId

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Payment amount must be greater than 0.")]
        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PaymentMethod PaymentMethod { get; set; } // Enum

        [Range(1, int.MaxValue, ErrorMessage = "Please select an account.")]
        public int AssetAccountId { get; set; }

        [StringLength(500, ErrorMessage = "Remarks cannot exceed 500 characters.")]
        public string? Remarks { get; set; }

        [StringLength(100, ErrorMessage = "Source ID cannot exceed 100 characters.")]
        public string? SourceId { get; set; }
    }

    public class PaymentHistoryDTO
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public PaymentMethod Method { get; set; } // Enum
        public string? ReferenceNumber { get; set; }
        public string AccountName { get; set; } = string.Empty;
    }

    // --- REQUESTS (PayMongo DTOs kept as is for API compatibility) ---
    public class PaymentSourceResponseDTO
    {
        public string SourceId { get; set; } = string.Empty;
        public string CheckoutUrl { get; set; } = string.Empty;
    }

    public class CreateSourceDTO
    {
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Amount must be greater than 0.")]
        public decimal Amount { get; set; }

        [StringLength(200, ErrorMessage = "Description cannot exceed 200 characters.")]
        public string Description { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Remarks cannot exceed 500 characters.")]
        public string? Remarks { get; set; }

        [Url(ErrorMessage = "Success URL must be a valid URL.")]
        [StringLength(2048, ErrorMessage = "Success URL cannot exceed 2048 characters.")]
        public string? SuccessUrl { get; set; }

        [Url(ErrorMessage = "Failed URL must be a valid URL.")]
        [StringLength(2048, ErrorMessage = "Failed URL cannot exceed 2048 characters.")]
        public string? FailedUrl { get; set; }
    }

    // --- PAYMONGO API MODELS ---
    public class PayMongoSourceRequest
    {
        [JsonPropertyName("data")]
        public SourceData Data { get; set; } = new SourceData();
    }

    public class SourceData
    {
        [JsonPropertyName("attributes")]
        public SourceAttributes Attributes { get; set; } = new SourceAttributes();
    }

    public class SourceAttributes
    {
        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "gcash";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "PHP";

        [JsonPropertyName("redirect")]
        public RedirectUrls Redirect { get; set; } = new RedirectUrls();

        [JsonPropertyName("billing")]
        public BillingInfo Billing { get; set; } = new BillingInfo();
    }

    public class RedirectUrls
    {
        [JsonPropertyName("success")]
        public string Success { get; set; } = string.Empty;

        [JsonPropertyName("failed")]
        public string Failed { get; set; } = string.Empty;

        [JsonPropertyName("checkout_url")]
        public string CheckoutUrl { get; set; } = string.Empty;
    }

    public class BillingInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    // --- RESPONSES ---
    public class PayMongoSourceResponse
    {
        [JsonPropertyName("data")]
        public ResponseData Data { get; set; } = new ResponseData();
    }

    public class ResponseData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("attributes")]
        public ResponseAttributes Attributes { get; set; } = new ResponseAttributes();
    }

    public class ResponseAttributes
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("redirect")]
        public RedirectUrls Redirect { get; set; } = new RedirectUrls();
    }

    // --- WEBHOOKS ---
    public class PayMongoWebhookEvent
    {
        [JsonPropertyName("data")]
        public WebhookData Data { get; set; } = new WebhookData();
    }

    public class WebhookData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("attributes")]
        public WebhookAttributes Attributes { get; set; } = new WebhookAttributes();
    }

    public class WebhookAttributes
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public ResponseData Data { get; set; } = new ResponseData();
    }
}
