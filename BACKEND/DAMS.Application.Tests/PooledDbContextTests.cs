using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// Program.cs registers the context with AddDbContextPool. Pooling refuses any options change made
/// in OnConfiguring, and it does so on first use, so the endpoint tests (plain AddDbContext) and the
/// service tests (new AppDbContext) would never notice. This builds the context the way the app does.
/// </summary>
public class PooledDbContextTests
{
    [Fact]
    public async Task The_context_works_when_pooled_with_the_project_list_cache_interceptor()
    {
        var services = new ServiceCollection();
        services.AddDbContextPool<AppDbContext>(options => options
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(
                ProjectListCacheTransactionInterceptor.Instance,
                ProjectListCacheMediaInterceptor.Instance));
        await using var provider = services.BuildServiceProvider();

        for (var lease = 0; lease < 2; lease++)
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await context.Projects.CountAsync());
        }
    }
}
