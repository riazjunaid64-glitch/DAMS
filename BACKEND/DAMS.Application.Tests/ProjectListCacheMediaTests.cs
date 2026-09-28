using DAMS.Domain.Entities;
using DAMS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAMS.Application.Tests;

/// <summary>
/// A project photo has to drop the cached project list. The version counter is process-wide,
/// so this checks the decision the interceptor makes rather than the shared number.
/// </summary>
public class ProjectListCacheMediaTests
{
    [Fact]
    public async Task SavingAProjectPhotoCountsAsAProjectListChange()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(ProjectListCacheMediaInterceptor.Instance)
            .Options;
        await using var db = new AppDbContext(options);

        Assert.False(ProjectListCacheMediaInterceptor.MediaRowsChanged(db));

        db.ProjectMedias.Add(new ProjectMedia { ProjectId = 1, MediaUrl = "/covers/tower.jpg", MediaType = "image", IsCover = true });
        Assert.True(ProjectListCacheMediaInterceptor.MediaRowsChanged(db));

        var before = ProjectListCache.Version;
        await db.SaveChangesAsync();
        Assert.True(ProjectListCache.Version > before);
    }
}
