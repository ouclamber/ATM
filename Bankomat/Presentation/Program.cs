using System.Globalization;
using Bankomat.Application.Abstractions;
using Bankomat.Application.Accounts;
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
    public static async Task<int> Main(string[] args)
    {
        // Служебный режим: получить BCrypt-хеш пароля администратора
        if (args.Length >= 1 && args[0] == "--hash")
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("Использование: Bankomat --hash \"новый-пароль\"");
                return 2;
            }
            Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(args[1]));
            return 0;
        }

        var culture = new CultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (!Console.IsInputRedirected)
            Console.InputEncoding = System.Text.Encoding.UTF8;

        var config = BuildConfiguration();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // корректно завершаемся и сбрасываем логи
            cts.Cancel();
        };

        try
        {
            Log.Information("Приложение запускается, окружение {Environment}", EnvironmentName());

            await using var provider = BuildServiceProvider(config);

            await InitializeDatabaseAsync(provider, config, cts.Token);

            Log.Information("Инициализация завершена, запуск меню");

            var main = provider.GetRequiredService<MainMenu>();
            await main.RunAsync(cts.Token);

            Log.Information("Приложение завершено штатно");
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Log.Information("Приложение остановлено пользователем (Ctrl+C)");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение упало с фатальной ошибкой");
            AnsiConsole.MarkupLine("[red]Фатальная ошибка:[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);

            if (ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException
                || ex.GetBaseException() is System.Net.Sockets.SocketException)
            {
                AnsiConsole.MarkupLine(
                    "\n[yellow]Похоже, PostgreSQL недоступен. Проверьте, что контейнер запущен " +
                    "([white]docker compose up -d[/]) и строку подключения ConnectionStrings:DefaultConnection.[/]");
            }

            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey(intercept: true);
            }
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static string EnvironmentName() =>
        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

    /// <summary>
    /// Порядок источников (каждый следующий переопределяет предыдущий):
    /// appsettings.json → appsettings.{Environment}.json → appsettings.local.json →
    /// переменные окружения (ConnectionStrings__DefaultConnection, Admin__PasswordHash, ...).
    /// Раньше читался только appsettings.json, хотя README обещал переопределение через переменные окружения.
    /// </summary>
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{EnvironmentName()}.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

    private static async Task InitializeDatabaseAsync(
        IServiceProvider provider, IConfiguration config, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();

        if (config.GetValue("Database:ApplyMigrationsOnStartup", true))
        {
            var db = scope.ServiceProvider.GetRequiredService<BankomatDbContext>();
            await db.Database.MigrateAsync(ct);
        }

        if (config.GetValue("Seed:Enabled", true))
        {
            var seed = scope.ServiceProvider.GetRequiredService<SeedHandler>();
            await seed.EnsureSeededAsync(ct);
        }
    }

    private static ServiceProvider BuildServiceProvider(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:DefaultConnection " +
                "(appsettings.json или переменная окружения ConnectionStrings__DefaultConnection)");

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        services.AddDbContext<BankomatDbContext>(opt =>
            opt.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

        // Abstractions
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPinHasher, BCryptPinHasher>();
        services.AddSingleton<IBankConfig, BankConfig>();
        services.AddSingleton<IAdminAuthService, AdminAuthService>();

        // Repositories
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IAtmRepository, AtmRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();

        // Handlers (по одному scope = один DbContext на операцию)
        services.AddScoped<LoginHandler>();
        services.AddScoped<ChangePinHandler>();
        services.AddScoped<GetAccountSummaryHandler>();
        services.AddScoped<WithdrawHandler>();
        services.AddScoped<DepositHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<RefillHandler>();
        services.AddScoped<SeedHandler>();

        // UI: меню живут всё время работы и сами создают scope на каждую операцию.
        // Раньше они были Scoped и резолвились из корневого провайдера (скрытая ошибка времени жизни).
        services.AddSingleton<UserSession>();
        services.AddSingleton<MainMenu>();
        services.AddSingleton<ClientMenu>();
        services.AddSingleton<AdminMenu>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
