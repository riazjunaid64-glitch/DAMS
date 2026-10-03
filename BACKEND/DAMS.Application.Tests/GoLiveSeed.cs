using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Tests;

/// <summary>Sets the go-live date on the single Finance settings row, the way a test used to commit an
/// opening balance set. The caller saves.</summary>
internal static class GoLiveSeed
{
    public static async Task SetAsync(AppDbContext context, DateTime date)
    {
        var settings = await context.FinanceSettings.SingleOrDefaultAsync(s => s.Id == FinanceSetting.SingletonId);
        if (settings == null)
        {
            settings = new FinanceSetting { Id = FinanceSetting.SingletonId };
            context.FinanceSettings.Add(settings);
        }
        settings.GoLiveDate = date.Date;
    }
}
