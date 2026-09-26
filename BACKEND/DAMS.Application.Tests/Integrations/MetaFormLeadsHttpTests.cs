using System.Net;
using System.Web;
using DAMS.Application.Services.Integrations;
using Xunit;
using static DAMS.Application.Tests.Integrations.MetaGraphClientHttpTests;

namespace DAMS.Application.Tests.Integrations;

/// <summary>KAN-35: reading a form's leads — the request Meta gets, and how its pages and refusals are read.</summary>
public class MetaFormLeadsHttpTests
{
    private const string Token = "form-leads-token";

    private static string LeadJson(string id) => $$"""
        { "id": "{{id}}", "created_time": "2026-09-20T10:00:00+0000", "form_id": "form-1", "platform": "fb",
          "field_data": [ { "name": "full_name", "values": [ "Buyer {{id}}" ] } ] }
        """;

    [Fact]
    public async Task FormLeads_AreFilteredByTimeAndFollowedAcrossPages()
    {
        var since = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        var handler = new FakeHandler(request => request.RequestUri!.Query.Contains("after=cursor-2")
            ? GraphJson($$"""{ "data": [ {{LeadJson("l-3")}} ] }""")
            : GraphJson($$"""
                { "data": [ {{LeadJson("l-1")}}, {{LeadJson("l-2")}} ],
                  "paging": { "next": "https://graph.facebook.com/v21.0/form-1/leads?after=cursor-2&access_token=leaked" } }
                """));

        var page = await DirectClient(handler).GetFormLeadsAsync("form-1", since, Token, CancellationToken.None);

        Assert.Equal(["l-1", "l-2", "l-3"], page.Leads.Select(l => l.LeadgenId));
        Assert.False(page.Truncated);
        Assert.Equal("Buyer l-1", page.Leads[0].FieldData.Single().Value);
        Assert.Equal(new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc), page.Leads[0].CreatedTime);

        var first = handler.Requests[0];
        Assert.StartsWith("/v21.0/form-1/leads", first.RequestUri!.AbsolutePath);
        var query = HttpUtility.ParseQueryString(first.RequestUri.Query);
        Assert.Equal("id,created_time,field_data,form_id,platform,is_organic,ad_id,adset_id,campaign_id", query["fields"]);
        Assert.Equal(
            $$"""[{"field":"time_created","operator":"GREATER_THAN","value":{{new DateTimeOffset(since).ToUnixTimeSeconds()}}}]""",
            query["filtering"]);
        Assert.NotNull(query["appsecret_proof"]);

        // Meta's own next link is followed with its token stripped; ours rides in the header.
        var second = handler.Requests[1];
        Assert.DoesNotContain("access_token", second.RequestUri!.Query);
        Assert.Equal(Token, second.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task RefusedAdIds_AreDroppedRatherThanCostingTheLeads()
    {
        var handler = new FakeHandler(request => RequestedFields(request).Contains("campaign_id")
            ? GraphError(HttpStatusCode.Forbidden, 200, AdsManagementRefusal)
            : GraphJson($$"""{ "data": [ {{LeadJson("l-1")}} ] }"""));

        var page = await DirectClient(handler).GetFormLeadsAsync("form-1", DateTime.UtcNow.AddDays(-1), Token, CancellationToken.None);

        Assert.Single(page.Leads);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AnExpiredToken_IsReportedAsAnAuthorizationFailure()
    {
        var handler = new FakeHandler(_ => GraphError(HttpStatusCode.BadRequest, 190, "Error validating access token"));

        await Assert.ThrowsAsync<MetaAuthorizationException>(() =>
            DirectClient(handler).GetFormLeadsAsync("form-1", DateTime.UtcNow.AddDays(-1), Token, CancellationToken.None));
        Assert.Single(handler.Requests);
    }
}
