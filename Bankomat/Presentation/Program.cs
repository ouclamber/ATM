using System.Globalization;
using Bankomat.Application.Abstractions;
using Bankomat.Application.AtmManagement;
using Bankomat.Application.Authentication;
using Bankomat.Application.Operations;
using Bankomat.Application.Seed;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Bankomat.Infrastructure.Configuration;
using Bankomat.Infrastructure.Persistence;
using Bankomat.Infrastructure.Persistence.Repositories;
using Bankomat.Infrastructure.Security;
using Bankomat.Presentation.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;

namespace Bankomat.Presentation;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--hash")
        {
            var plain = args.Length >= 2 ? args[1] : "admin123";
            Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(plain));
            return;
        }

        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        try
        {
            Log.Information("Приложение запускается");

            var provider = BuildServiceProvider(config);

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BankomatDbContext>();
                await db.Database.MigrateAsync();

                var seed = scope.ServiceProvider.GetRequiredService<SeedHandler>();
                await seed.EnsureSeededAsync();
            }

            Log.Information("Инициализация завершена, запуск меню");

            var main = provider.GetRequiredService<MainMenu>();
            await main.RunAsync();

            Log.Information("Приложение завершено штатно");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение упало с фатальной ошибкой");
            AnsiConsole.WriteException(ex);
            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static ServiceProvider BuildServiceProvider(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
        });

        services.AddDbContext<BankomatDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        // Abstractions
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPinHasher, BCryptPinHasher>();
        services.AddSingleton<IBankConfig, BankConfig>();
        services.AddSingleton<IAdminAuthService, AdminAuthService>();

        // Repositories
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IAtmRepository, AtmRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();

        // Handlers
        services.AddScoped<LoginHandler>();
        services.AddScoped<ChangePinHandler>();
        services.AddScoped<WithdrawHandler>();
        services.AddScoped<DepositHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<RefillHandler>();
        services.AddScoped<SeedHandler>();

        // UI
        services.AddSingleton<UserSession>();
        services.AddScoped<MainMenu>();
        services.AddScoped<ClientMenu>();
        services.AddScoped<AdminMenu>();

        return services.BuildServiceProvider();
    }
}