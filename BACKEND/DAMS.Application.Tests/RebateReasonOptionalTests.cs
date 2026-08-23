using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The rebate reason is optional on screen, and the service already falls back to describing the
/// entry itself when none is typed. None of that is reachable if the request never gets to the
/// action.
/// <para>
/// This is the layer the service suites cannot see. <c>[ApiController]</c> treats a non-nullable
/// reference-typed property as implicitly required, so a DTO that declares <c>string Reason</c>
/// makes MVC refuse a body carrying <c>reason: null</c> before the action runs — and it refuses it
/// with a <c>ValidationProblemDetails</c>, whose shape is <c>{ title, status, errors }</c> with no
/// <c>message</c>. The commissions-and-rebates screen reads <c>message</c>, so the operator got the
/// bare fallback "The financial workflow request could not be completed." with nothing naming the
/// field. Every service-level test passed throughout, because they construct the DTO in memory and
/// never cross model binding.
/// </para>
/// </summary>
public sealed class RebateReasonOptionalTests : IClassFixture<IdempotentMoneyOperationTests.ApiFactory>
{
    private readonly IdempotentMoneyOperationTests.ApiFactory _factory;

    public RebateReasonOptionalTests(IdempotentMoneyOperationTests.ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ARebateWithNoTypedReason_ReachesTheAction_InsteadOfBeingRefusedAtModelBinding()
    {
        var response = await Admin().PostAsJsonAsync(
            "/api/finance/commissions-rebates/bookings/1/rebates", Body(reason: null));

        // The booking does not exist in this in-memory host, so a refusal is expected — but it has
        // to be the ACTION'S refusal, which names what is wrong, not MVC's silent one.
        await AssertRefusalIsReadable(response);
    }

    [Fact]
    public async Task ARebateWithATypedReason_IsRefusedTheSameReadableWay()
    {
        // The control: proves the assertion above is about the null reason and not about the
        // endpoint refusing everything with an unreadable body.
        var response = await Admin().PostAsJsonAsync(
            "/api/finance/commissions-rebates/bookings/1/rebates", Body(reason: "Goodwill on a delayed handover"));

        await AssertRefusalIsReadable(response);
    }

    /// <summary>
    /// What the screen needs from any refusal: a <c>message</c> to show, and no validation
    /// <c>errors</c> bag, which is the signature of the request dying before the action.
    /// </summary>
    private static async Task AssertRefusalIsReadable(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;

        Assert.False(root.TryGetProperty("errors", out _),
            $"Model binding refused the request before the action ran: {body}");
        Assert.True(root.TryGetProperty("message", out var message),
            $"The refusal carried nothing the screen could show: {body}");
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
    }

    // Exactly what the booking screen sends: fixed amount, no percentage, no adjustment, and the
    // method chosen later rather than here.
    private static object Body(string? reason) => new
    {
        calculationType = "FixedAmount",
        calculationBasis = "AgreedSalePrice",
        percentageRate = (decimal?)null,
        fixedAmount = 20000m,
        manualBasisAmount = (decimal?)null,
        adjustmentAmount = 0m,
        adjustmentReason = (string?)null,
        reason,
        notes = (string?)null,
        method = "OutstandingBalanceReduction"
    };

    private HttpClient Admin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.GenerateAccessToken(
                new User { UserId = 42, Email = "actor@dams.test", FullName = "Rebate Admin" }, "Admin"));
        return client;
    }
}
