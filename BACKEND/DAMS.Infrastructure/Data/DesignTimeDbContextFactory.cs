using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DAMS.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var apiDir = FindApiDirectoryWithAppSettings();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// dotnet ef runs with varied current directories (repo root vs DAMS.Api folder).
    /// Always resolve BACKEND\DAMS.Api (or *\DAMS.Api) that contains appsettings.json.
    /// </summary>
    private static string FindApiDirectoryWithAppSettings()
    {
        var cwd = Directory.GetCurrentDirectory();

        static bool IsApiFolder(string path, out string verified)
        {
            verified = path;
            return File.Exists(Path.Combine(path, "appsettings.json"))
                   && File.Exists(Path.Combine(path, "DAMS.Api.csproj"));
        }

        if (IsApiFolder(cwd, out var direct))
            return direct;

        var nested = Path.Combine(cwd, "DAMS.Api");
        if (Directory.Exists(nested) && IsApiFolder(nested, out var verifiedNested))
            return verifiedNested;

        for (var dir = Directory.GetParent(cwd); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "DAMS.Api");
            if (Directory.Exists(candidate) && IsApiFolder(candidate, out var verified))
                return verified;
        }

        throw new InvalidOperationException(
            $"Could not find DAMS.Api folder with appsettings.json. Checked from: {cwd}. " +
            "Run EF tools from the solution folder or DAMS.Api, or fix the repo layout.");
    }
}
