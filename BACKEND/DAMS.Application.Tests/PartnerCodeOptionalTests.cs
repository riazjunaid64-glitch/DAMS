using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// The partner code is optional when adding a partner, and the service already makes one when it
/// is empty. None of that is reachable if the request never gets to the action: with nullable
/// types on, <c>[ApiController]</c> treats a non-nullable <c>string InternalCode</c> as required
/// and refuses a body without it before the service runs, with a validation answer that carries no
/// <c>message</c>. The same trap is documented in <see cref="RebateReasonOptionalTests"/>.
/// </summary>
public sealed class PartnerCodeOptionalTests : IClassFixture<IdempotentMoneyOperationTests.ApiFactory>
{
    private readonly IdempotentMoneyOperationTests.ApiFactory _factory;

    public PartnerCodeOptionalTests(IdempotentMoneyOperationTests.ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task APartnerWithNoCodeKey_ReachesTheService_AndGetsAMadeCode()
    {
        await EnsureDatabaseAsync();
        var response = await Admin().PostAsJsonAsync("/api/finance/commissions-rebates/partners",
            new { name = $"No code key {Guid.NewGuid():N}", partnerType = "Broker" });

        await AssertPartnerWithMadeCode(response);
    }

    [Fact]
    public async Task APartnerWithANullCode_ReachesTheService_AndGetsAMadeCode()
    {
        await EnsureDatabaseAsync();
        var response = await Admin().PostAsJsonAsync("/api/finance/commissions-rebates/partners",
            new { name = $"Null code {Guid.NewGuid():N}", partnerType = "Dealer", internalCode = (string?)null });

        await AssertPartnerWithMadeCode(response);
    }

    [Fact]
    public async Task APartnerWithATooLongName_IsRefusedWithAMessage_NotAnErrorsBag()
    {
        await EnsureDatabaseAsync();
        var response = await Admin().PostAsJsonAsync("/api/finance/commissions-rebates/partners",
            new { name = new string('n', 201), partnerType = "Broker" });

        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        Assert.False(response.IsSuccessStatusCode, body);
        Assert.False(root.TryGetProperty("errors", out _), $"Model binding refused the request before the action ran: {body}");
        Assert.True(root.TryGetProperty("message", out var message), body);
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
    }

    private static async Task AssertPartnerWithMadeCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"The partner was refused: {response.StatusCode} {body}");
        var root = JsonDocument.Parse(body).RootElement;
        Assert.False(root.TryGetProperty("errors", out _), body);
        Assert.StartsWith("PTR-", root.GetProperty("internalCode").GetString());
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    private HttpClient Admin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            TestAccessSessions.Issue(_factory.Services, 42, "Admin", "actor@dams.test", "Partner Admin"));
        return client;
    }
}
