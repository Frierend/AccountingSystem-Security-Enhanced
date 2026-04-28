using System.Net;
using System.Text;
using AccountingSystem.API.Controllers;
using AccountingSystem.API.Middleware;
using AccountingSystem.API.Services;
using AccountingSystem.API.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AccountingSystem.API.Tests;

public class AuditLoggingSafetyTests
{
    [Fact]
    public void AuditLogSanitizer_WhenSerializingSensitiveFields_ShouldRedactValues()
    {
        var metadata = new
        {
            username = "admin@example.com",
            password = "REDACTED_TEST_VALUE",
            refreshToken = "refresh-token-value",
            apiKey = "api-key-value"
        };

        var serialized = AuditLogSanitizer.SerializeAndTrim(metadata);

        serialized.Should().Contain("[REDACTED]");
        serialized.Should().NotContain("REDACTED_TEST_VALUE");
        serialized.Should().NotContain("refresh-token-value");
        serialized.Should().NotContain("api-key-value");
    }

    [Fact]
    public async Task AuditMiddleware_WhenMutationSucceeds_ShouldStoreSanitizedMetadataOnly()
    {
        var dbContext = TestHelpers.CreateContext();
        var payload = "{\"email\":\"user@example.com\",\"password\":\"REDACTED_TEST_VALUE\",\"apiKey\":\"api-key-value\"}";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/users";
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Items["UserId"] = "25";
        httpContext.Items["CompanyId"] = "10";

        var middleware = new AuditMiddleware(_ =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, Mock.Of<ILogger<AuditMiddleware>>());

        await middleware.Invoke(httpContext, dbContext);

        var auditLog = await dbContext.AuditLogs.IgnoreQueryFilters().SingleAsync();
        auditLog.Action.Should().Be("USER-CREATE");
        auditLog.CompanyId.Should().Be(10);
        auditLog.Changes.Should().Contain("\"category\":\"System\"");
        auditLog.Changes.Should().Contain("\"hasBody\":true");
        auditLog.Changes.Should().NotContain("REDACTED_TEST_VALUE");
        auditLog.Changes.Should().NotContain("api-key-value");
    }

    [Fact]
    public async Task AuthSecurityAuditService_WhenWritingEvent_ShouldPersistSecurityCategory()
    {
        var dbContext = TestHelpers.CreateContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/auth/login";
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var service = new AuthSecurityAuditService(
            dbContext,
            accessor,
            Mock.Of<ILogger<AuthSecurityAuditService>>());

        await service.WriteAsync(
            "AUTH-LOGIN-FAILURE",
            companyId: 10,
            email: "user@example.com",
            reason: "InvalidPassword");

        var auditLog = await dbContext.AuditLogs.IgnoreQueryFilters().SingleAsync();
        auditLog.Action.Should().Be("AUTH-LOGIN-FAILURE");
        auditLog.Changes.Should().Contain("\"category\":\"Security\"");
    }

    [Fact]
    public async Task PaymentWebhook_WhenSignatureIsInvalid_ShouldWriteSecurityAuditEvent()
    {
        var paymentService = new Mock<IPaymentService>();
        paymentService
            .Setup(service => service.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        var auditService = new Mock<IAuthSecurityAuditService>();
        auditService
            .Setup(service => service.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = new PaymentController(
            paymentService.Object,
            auditService.Object,
            Mock.Of<ILogger<PaymentController>>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.ControllerContext.HttpContext.Request.Body =
            new MemoryStream(Encoding.UTF8.GetBytes("{\"data\":{\"id\":\"evt_123\"}}"));

        var result = await controller.HandleWebhook();

        result.Should().BeOfType<UnauthorizedObjectResult>();
        auditService.Verify(service => service.WriteAsync(
            "SECURITY-PAYMONGO-WEBHOOK-SIGNATURE-FAILURE",
            It.IsAny<int?>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            "InvalidOrMissingSignature",
            It.IsAny<int?>(),
            It.IsAny<DateTime?>(),
            "PayMongoWebhookSignature"), Times.Once);
    }
}
