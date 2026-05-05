using AccountingSystem.API.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;

namespace AccountingSystem.API.Tests;

public class RecaptchaConfigControllerTests
{
    [Fact]
    public void GetRecaptchaConfig_WhenSiteKeyConfigured_ShouldReturnOnlyPublicSiteKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recaptcha:SiteKey"] = "public-site-key",
                ["Recaptcha:SecretKey"] = "server-secret"
            })
            .Build();
        var controller = new AuthController(Mock.Of<IAuthService>(), configuration);

        var result = controller.GetRecaptchaConfig();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<RecaptchaConfigDTO>().Subject;
        dto.SiteKey.Should().Be("public-site-key");
        ok.Value!.GetType().GetProperty("SecretKey").Should().BeNull();
    }

    [Fact]
    public void GetRecaptchaConfig_WhenSiteKeyMissing_ShouldFailClosed()
    {
        var controller = new AuthController(Mock.Of<IAuthService>(), new ConfigurationBuilder().Build());

        var result = controller.GetRecaptchaConfig();

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(503);
    }
}
