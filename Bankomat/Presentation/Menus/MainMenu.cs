using Bankomat.Application.Abstractions;
using Bankomat.Application.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Bankomat.Presentation.Menus;

public class MainMenu
{
    private readonly IServiceProvider _sp;
    private readonly UserSession _session;
    private readonly IBankConfig _config;

    public MainMenu(IServiceProvider sp, UserSession session, IBankConfig config)
    {
        _sp = sp;
        _session = session;
        _config = config;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            Console.Clear();
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
                case "Войти как клиент":        await LoginAsync(); break;
                case "Административная панель": await AdminAsync(); break;
                case "Выход":
                    Console.Clear();
                    AnsiConsole.Write(new FigletText("До свидания!").Centered().Color(Color.Green));
                    return;
            }
        }
    }

    private async Task LoginAsync()
    {
        Console.Clear();

        var card = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Номер карты:[/]")
                .Validate(c =>
                {
                    var clean = c.Replace(" ", "").Replace("-", "");
                    return clean.Length == 16 && clean.All(char.IsDigit)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]16 цифр[/]");
                }));

        var pin = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]PIN:[/]").Secret());

        using var scope = _sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var result = await handler.HandleAsync(card, pin, _config.DefaultAtmId);

        if (result.IsFailure)
        {
            AnsiConsole.MarkupLine($"[red]{result.Error.Message}[/]");
            await Task.Delay(2000);
            return;
        }

        _session.CurrentAccount = result.Value;
        AnsiConsole.MarkupLine($"[green]Добро пожаловать, {result.Value.HolderName}![/]");
        await Task.Delay(1200);

        var clientMenu = scope.ServiceProvider.GetRequiredService<ClientMenu>();
        await clientMenu.RunAsync();
    }

    private async Task AdminAsync()
    {
        Console.Clear();

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

        using var scope = _sp.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAdminAuthService>();

        if (!auth.Verify(username, password))
        {
            Console.Clear();
            AnsiConsole.MarkupLine("[red]Неверный логин или пароль[/]");
            await Task.Delay(2000);
            return;
        }

        Console.Clear();
        AnsiConsole.MarkupLine("[green]Добро пожаловать, администратор[/]");
        await Task.Delay(1000);

        var adminMenu = scope.ServiceProvider.GetRequiredService<AdminMenu>();
        await adminMenu.RunAsync();
    }
}