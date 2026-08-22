using System.Security.Cryptography;
using System.Text;
using DAMS.Api.Controllers;
using DAMS.Application.Common;
using DAMS.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DAMS.Application.Tests.Integrations;

/// <summary>
/// The controller in isolation from the intake service, so the one behaviour Meta's retry
/// contract actually depends on — a non-2xx exactly when persistence failed — can be proven
/// without a database or the full HTTP pipeline.
/// </summary>
public class MetaWebhookControllerTests
{
    private const string AppSecret = "controller-test-secret";

    private static MetaIntegrationOptions Options() => new()
    {
        AppId = "app", AppSecret = AppSecret, WebhookVerifyToken = "verify",
        OAuthCallbackUrl = "https://dams.test/callback", MaxWebhookBodyBytes = 524288
    };

    private static MetaWebhookController BuildController(IMetaWebhookIntakeService intake, string body)
    {
        var controller = new MetaWebhookController(intake, Options(), NullLogger<MetaWebhookController>.Instance);

        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var httpContext = new DefaultHttpContext
        {
            Request =
            {
                Body = new MemoryStream(bodyBytes),
                ContentLength = bodyBytes.Length
            }
        };
        httpContext.Request.Headers["X-Hub-Signature-256"] = signature;

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class ThrowingIntake : IMetaWebhookIntakeService
    {
        public Task<int> RecordAsync(string rawBody, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated database outage.");
    }

    private sealed class WorkingIntake(int recorded) : IMetaWebhookIntakeService
    {
        public Task<int> RecordAsync(string rawBody, CancellationToken cancellationToken = default) =>
            Task.FromResult(recorded);
    }

    [Fact]
    public async Task APersistenceFailure_Returns500SoMetaRetries_NeverA200()
    {
        var controller = BuildController(new ThrowingIntake(), "{}");

        var result = await controller.Receive(CancellationToken.None);

        // A 200 here would tell Meta the delivery is handled and it would never be sent again —
        // exactly wrong when nothing was actually saved.
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public async Task ASuccessfulRecord_Returns200()
    {
        var controller = BuildController(new WorkingIntake(1), "{}");

        var result = await controller.Receive(CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task NothingToRecord_StillReturns200()
    {
        // A signed delivery that simply contained no leadgen change (a different webhook
        // field, for example) is not a failure — RecordAsync returns 0 normally.
        var controller = BuildController(new WorkingIntake(0), "{}");

        var result = await controller.Receive(CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }
}
