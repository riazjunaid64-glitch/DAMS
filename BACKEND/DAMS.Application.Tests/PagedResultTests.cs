using DAMS.Application.DTOs.FinanceDtos;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// TotalCount is optional. A service that already counted passes it in; every other page still
/// reports HasMore from the extra row and leaves the total unset.
/// </summary>
public class PagedResultTests
{
    [Fact]
    public void Page_AlwaysSetsHasMore_AndStoresTotalOnlyWhenTheCallerCounted()
    {
        var page = PagedResult<int>.Page(new[] { 1, 2, 3 }, 2, totalCount: 10);

        Assert.Equal(new[] { 1, 2 }, page.Items);
        Assert.True(page.HasMore);
        Assert.Equal(10, page.TotalCount);

        var noTotal = PagedResult<int>.Page(new[] { 1, 2 }, 2);

        Assert.Equal(new[] { 1, 2 }, noTotal.Items);
        Assert.False(noTotal.HasMore);
        Assert.Null(noTotal.TotalCount);
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(20, false, false)]
    [InlineData(20, true, true)]
    public void IncludeTotal_OnlyWhenTheCallerAsks(int skip, bool includeTotal, bool expected)
    {
        Assert.Equal(expected, PagedResult<int>.IncludeTotal(skip, includeTotal));
    }
}
