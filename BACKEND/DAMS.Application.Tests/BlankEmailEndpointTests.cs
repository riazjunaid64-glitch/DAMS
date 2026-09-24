using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DAMS.Application.Interfaces;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Forms send every field, so a lead or customer captured without an email arrives with
/// <c>"email": ""</c>. Where email is optional, a blank one must mean "no email" — not an invalid
/// address that the API rejects before the service ever sees the request.
/// </summary>
public sealed class BlankEmailEndpointTests : IClassFixture<IdempotentMoneyOperationTests.ApiFactory>
{
    private readonly IdempotentMoneyOperationTests.ApiFactory _factory;

    public BlankEmailEndpointTests(IdempotentMoneyOperationTests.ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public async Task APhoneOnlyLead_WithABlankEmail_IsCreatedWithNoEmail(string blankEmail)
    {
        var phone = UniquePhone();
        var response = await Admin().PostAsync("/api/leads", Json(
            $$"""{"firstName":"Phone Only","phone":"{{phone}}","whatsappNumber":"","email":{{blankEmail}},"sourceCode":"manual"}"""));

        var lead = await CreatedLeadAsync(response);
        Assert.Equal(JsonValueKind.Null, lead.GetProperty("email").ValueKind);
        Assert.Null((await StoredAsync(lead.GetProperty("id").GetInt32())).Email);
    }

    [Fact]
    public async Task AWhatsappOnlyLead_WithABlankEmail_IsCreatedWithNoEmail()
    {
        var response = await Admin().PostAsync("/api/leads", Json(
            $$"""{"firstName":"WhatsApp Only","phone":"","whatsappNumber":"{{UniquePhone()}}","email":"","sourceCode":"manual"}"""));

        var lead = await CreatedLeadAsync(response);
        Assert.Equal(JsonValueKind.Null, lead.GetProperty("email").ValueKind);
    }

    // An omitted property never reaches the Email setter, unlike "" — so it is covered separately.
    [Theory]
    [InlineData("phone")]
    [InlineData("whatsappNumber")]
    public async Task ALead_WithNoEmailPropertyAtAll_IsCreatedWithNoEmail(string contactField)
    {
        var response = await Admin().PostAsync("/api/leads", Json(
            $$"""{"firstName":"No Email Field","{{contactField}}":"{{UniquePhone()}}","sourceCode":"manual"}"""));

        var lead = await CreatedLeadAsync(response);
        Assert.Null((await StoredAsync(lead.GetProperty("id").GetInt32())).Email);
    }

    [Fact]
    public async Task ACustomer_WithNoEmailPropertyAtAll_IsCreatedWithNoEmail()
    {
        var response = await Admin().PostAsync("/api/Customer", Json(
            $$"""{"fullName":"No Email Field Customer","phone":"{{UniquePhone()}}"}"""));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}: {body}");
        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers
            .AsNoTracking().SingleAsync(c => c.Id == id)).Email);
    }

    [Fact]
    public async Task ABookingForANewCustomer_WithNoEmailPropertyAtAll_IsNotAnEmailError()
    {
        var response = await Admin().PostAsync("/api/Booking", Json(
            $$$"""{"unitId":999999,"source":"WalkIn","newCustomer":{"fullName":"Walk In","phone":"{{{UniquePhone()}}}"}}"""));

        var errors = ValidationErrors(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(errors.Keys, k => k.EndsWith("Email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EditingALead_AndClearingItsEmail_Succeeds()
    {
        var client = Admin();
        var phone = UniquePhone();
        var created = await CreatedLeadAsync(await client.PostAsync("/api/leads", Json(
            $$"""{"firstName":"Had Email","phone":"{{phone}}","email":"{{Guid.NewGuid():N}}@example.com","sourceCode":"manual"}""")));
        var id = created.GetProperty("id").GetInt32();

        var response = await client.PutAsync($"/api/leads/{id}", Json(
            $$"""{"firstName":"Had Email","phone":"{{phone}}","whatsappNumber":"","email":""}"""));

        Assert.True(response.IsSuccessStatusCode,
            $"Expected success, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Null((await StoredAsync(id)).Email);
    }

    [Fact]
    public async Task AnInvalidEmail_IsStillRejected_OnCreateAndOnEdit()
    {
        var client = Admin();
        var phone = UniquePhone();

        var create = await client.PostAsync("/api/leads", Json(
            $$"""{"firstName":"Bad Email","phone":"{{phone}}","email":"not-an-email","sourceCode":"manual"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Contains("Email", await create.Content.ReadAsStringAsync());

        var created = await CreatedLeadAsync(await client.PostAsync("/api/leads", Json(
            $$"""{"firstName":"Bad Email","phone":"{{phone}}","email":"","sourceCode":"manual"}""")));
        var edit = await client.PutAsync($"/api/leads/{created.GetProperty("id").GetInt32()}", Json(
            $$"""{"firstName":"Bad Email","phone":"{{phone}}","email":"not-an-email"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    [Fact]
    public async Task AValidEmail_IsStillStoredNormalized()
    {
        var response = await Admin().PostAsync("/api/leads", Json(
            $$"""{"firstName":"Has Email","email":"  Mixed.{{Guid.NewGuid():N}}@Example.com ","sourceCode":"manual"}"""));

        var lead = await CreatedLeadAsync(response);
        var email = (await StoredAsync(lead.GetProperty("id").GetInt32())).Email;
        Assert.StartsWith("mixed.", email);
        Assert.EndsWith("@example.com", email);
    }

    [Fact]
    public async Task ACustomer_WithABlankEmail_IsCreatedAndEditedWithNoEmail()
    {
        var client = Admin();
        var phone = UniquePhone();

        var create = await client.PostAsync("/api/Customer", Json(
            $$"""{"fullName":"No Email Customer","phone":"{{phone}}","email":""}"""));
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, $"Expected success, got {create.StatusCode}: {createBody}");
        var id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetInt32();

        var edit = await client.PutAsync($"/api/Customer/{id}", Json(
            $$"""{"fullName":"No Email Customer","phone":"{{phone}}","email":"   ","status":"Active"}"""));
        Assert.True(edit.IsSuccessStatusCode,
            $"Expected success, got {edit.StatusCode}: {await edit.Content.ReadAsStringAsync()}");

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers
            .AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Null(stored.Email);
    }

    [Fact]
    public async Task ACustomer_WithAnInvalidEmail_IsStillRejected()
    {
        var response = await Admin().PostAsync("/api/Customer", Json(
            $$"""{"fullName":"Bad Email Customer","phone":"{{UniquePhone()}}","email":"not-an-email"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(ValidationErrors(await response.Content.ReadAsStringAsync()).ContainsKey("Email"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("not-an-email", true)]
    public async Task ABookingForANewCustomer_OnlyRejectsAnEmailThatIsActuallyInvalid(string email, bool rejected)
    {
        // The booking itself fails later for other reasons (no such unit); what matters here is
        // whether model validation calls the new customer's email invalid.
        var response = await Admin().PostAsync("/api/Booking", Json(
            $$$"""{"unitId":999999,"source":"WalkIn","newCustomer":{"fullName":"Walk In","phone":"{{{UniquePhone()}}}","email":"{{{email}}}"}}"""));

        var errors = ValidationErrors(await response.Content.ReadAsStringAsync());
        Assert.Equal(rejected, errors.Keys.Any(k => k.EndsWith("Email", StringComparison.OrdinalIgnoreCase)));
    }

    private static Dictionary<string, JsonElement> ValidationErrors(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];
    }

    private HttpClient Admin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            tokens.GenerateAccessToken(
                new User { UserId = 43, Email = "lead-admin@dams.test", FullName = "Lead Admin" }, "Admin"));
        return client;
    }

    // Unique per test, so duplicate matching never links one test's lead to another's.
    private static string UniquePhone() => $"0300{Random.Shared.Next(1_000_000, 9_999_999)}";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> CreatedLeadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}: {body}");
        var result = JsonDocument.Parse(body).RootElement;
        Assert.True(result.TryGetProperty("lead", out var lead) && lead.ValueKind == JsonValueKind.Object,
            $"No lead was created: {body}");
        return lead;
    }

    private async Task<Lead> StoredAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Leads
            .AsNoTracking().SingleAsync(l => l.Id == id);
    }
}
