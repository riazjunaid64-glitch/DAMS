using DAMS.Application.Common;
using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DAMS.Application.Services
{
    /// <summary>
    /// Reads the finance singleton. Shared by every service that needs to know where the
    /// financial year starts, and tolerant of the row being absent so an unseeded store (tests,
    /// a fresh in-memory database) falls back to the July default rather than throwing inside a
    /// tax calculation.
    /// </summary>
    internal static class FinanceSettingsQuery
    {
        public static async Task<int> FinancialYearStartMonthAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            var month = await context.FinanceSettings.AsNoTracking()
                .Where(s => s.Id == FinanceSetting.SingletonId)
                .Select(s => (int?)s.FinancialYearStartMonth)
                .SingleOrDefaultAsync(cancellationToken);
            return FinancialYear.Normalise(month ?? FinancialYear.DefaultStartMonth);
        }
    }
}
