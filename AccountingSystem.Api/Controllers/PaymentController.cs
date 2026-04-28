using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AccountingSystem.API.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly IAuthSecurityAuditService _authSecurityAuditService;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IPaymentService paymentService,
            IAuthSecurityAuditService authSecurityAuditService,
            ILogger<PaymentController> logger)
        {
            _paymentService = paymentService;
            _authSecurityAuditService = authSecurityAuditService;
            _logger = logger;
        }

        [HttpPost("paymongo-source")]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<ActionResult<PaymentSourceResponseDTO>> CreateSource(
            [FromBody] CreateSourceDTO request)
        {
            try
            {
                var result = await _paymentService.CreatePaymentSourceAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayMongo source creation failed");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Unable to initialize payment source right now. Please try again later." });
            }
        }

        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> HandleWebhook()
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();
            var signature = Request.Headers["Paymongo-Signature"].ToString();
            if (string.IsNullOrWhiteSpace(signature))
            {
                signature = Request.Headers["x-paymongo-signature"].ToString();
            }

            if (!_paymentService.VerifyWebhookSignature(signature, json))
            {
                _logger.LogWarning("Rejected PayMongo webhook request due to invalid signature.");
                await _authSecurityAuditService.WriteAsync(
                    "SECURITY-PAYMONGO-WEBHOOK-SIGNATURE-FAILURE",
                    reason: "InvalidOrMissingSignature",
                    policy: "PayMongoWebhookSignature");
                return Unauthorized(new { error = "Invalid webhook signature." });
            }

            _logger.LogInformation("Received Webhook from PayMongo");
            return Ok();
        }
    }
}
