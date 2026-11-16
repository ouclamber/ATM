using Bankomat.Application.Abstractions;
using Bankomat.Application.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Bankomat.Presentation.Menus;

public class MainMenu
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MainMenu>();

    private readonly IServiceScopeFactory _scopes;
    private readonly UserSession _session;
    private readonly IBankConfig _config;
    private readonly IAdminAuthService _adminAuth;
    private readonly ClientMenu _clientMenu;
    private readonly AdminMenu _adminMenu;

    public MainMenu(
        IServiceScopeFactory scopes,
        UserSession session,
        IBankConfig config,
        IAdminAuthService adminAuth,
        ClientMenu clientMenu,
        AdminMenu adminMenu)
    {
        _scopes = scopes;
        _session = session;
        _config = config;
        _adminAuth = adminAuth;
        _clientMenu = clientMenu;
        _adminMenu = adminMenu;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            Ui.Clear();
            AnsiConsole.Write(new FigletText("БАНКОМАТ").Centered().Color(Color.Blue));

            AnsiConsole.Write(new Panel("[yellow]Система управления банкоматом[/]")
                .Border(BoxBorder.Rounded)
                .Header("[white]Главное меню[/]")
                .HeaderAlignment(Justify.Center));

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Выберите действие:[/]")
                    .PageSize(10)
                    .AddChoices(
                        "Войти как клиент",
                        "Административная панель",
                        "Выход"));

            switch (choice)
            {
                case "Войти как клиент":
                    await Ui.GuardAsync(() => LoginAsync(ct), Log, "Вход клиента");
                    _session.Logout(); // гарантированный выход даже после сбоя
                    break;
                case "Административная панель":
                    await Ui.GuardAsync(() => AdminAsync(ct), Log, "Вход администратора");
                    break;
                case "Выход":
                    Ui.Clear();
                    AnsiConsole.Write(new FigletText("До свидания!").Centered().Color(Color.Green));
                    return;
            }
        }
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        Ui.Clear();

        var card = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Номер карты:[/]")
                .Validate(c => Ui.IsValidCard(c)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Нужно 16 цифр (пробелы и дефисы допускаются)[/]")));

        var pin = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]PIN:[/]").Secret());

        using var scope = _scopes.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var result = await handler.HandleAsync(card, pin, _config.DefaultAtmId, ct);

        if (result.IsFailure)
        {
            Ui.Error(result.Error.Message);
            await Task.Delay(2000, ct);
            return;
        }

        _session.Start(result.Value);
        Ui.Success($"Добро пожаловать, {result.Value.HolderName}!");
        await Task.Delay(1200, ct);

        await _clientMenu.RunAsync(ct);
    }

    private async Task AdminAsync(CancellationToken ct)
    {
        Ui.Clear();

        if (_adminAuth.LockoutRemaining is { } locked)
        {
            Ui.Error($"Вход временно заблокирован из-за неверных попыток. Повторите через {Math.Ceiling(locked.TotalMinutes)} мин.");
            await Task.Delay(2500, ct);
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]ВХОД В АДМИН-ПАНЕЛЬ[/]")
            .Border(BoxBorder.Rounded)
            .Header("[white]Требуется авторизация[/]")
            .HeaderAlignment(Justify.Center));

        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Логин:[/]")
                .PromptStyle("green"));

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Пароль:[/]")
                .PromptStyle("red")
                .Secret());

        if (!_adminAuth.Verify(username, password))
        {
            Log.Warning("Admin: неудачная попытка входа под логином {Username}", username);
            Ui.Clear();
            Ui.Error(_adminAuth.LockoutRemaining is { } lockout
                ? $"Слишком много неверных попыток. Вход заблокирован на {Math.Ceiling(lockout.TotalMinutes)} мин."
                : "Неверный логин или пароль");
            await Task.Delay(2000, ct);
            return;
        }

        Log.Information("Admin: успешный вход под логином {Username}", username);
        Ui.Clear();
        Ui.Success("Добро пожаловать, администратор");
        await Task.Delay(1000, ct);

        await _adminMenu.RunAsync(ct);
    }
}
