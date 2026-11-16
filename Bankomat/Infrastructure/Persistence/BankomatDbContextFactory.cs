using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Bankomat.Infrastructure.Persistence;

/// <summary>Используется только инструментом dotnet-ef (миграции) во время разработки.</summary>
public class BankomatDbContextFactory : IDesignTimeDbContextFactory<BankomatDbContext>
{
    public BankomatDbContext CreateDbContext(string[] args)
    {
        // dotnet ef может запускаться как из папки проекта, так и из корня решения
        var basePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory()
            : AppContext.BaseDirectory;

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<BankomatDbContext>()
            .UseNpgsql(config.GetConnectionString("DefaultConnection"))
            .Options;

        return new BankomatDbContext(options);
    }
}
