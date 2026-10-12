using Bankomat.Application.Abstractions;
using Bankomat.Application.Authentication;
using Bankomat.Application.Operations;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Common;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Bankomat.Domain.Atms;

namespace Bankomat.Presentation.Menus;

public class ClientMenu
{
    private readonly IServiceProvider _sp;
    private readonly UserSession _session;
    private readonly IBankConfig _config;

    public ClientMenu(IServiceProvider sp, UserSession session, IBankConfig config)
    {
        _sp = sp;
        _session = session;
        _config = config;
    }

    public async Task RunAsync()
    {
        while (_session.IsAuthenticated)
        {
            Console.Clear();

            var account = _session.CurrentAccount!;

            AnsiConsole.Write(new Panel($"[yellow]КАРТА: {account.CardNumber.Formatted()}[/]")
                .Border(BoxBorder.Rounded)
                .Header($"[white]{account.HolderName}[/]")
                .HeaderAlignment(Justify.Center));

            AnsiConsole.Write(new Panel($"[green]{account.Balance}[/]")
                .Border(BoxBorder.Rounded)
                .Header("[white]Баланс[/]")
                .HeaderAlignment(Justify.Center));

            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Выберите операцию:[/]")
                    .PageSize(10)
                    .AddChoices(
                        "Проверить баланс",
                        "Снять наличные",
                        "Внести наличные",
                        "Перевести средства",
                        "История операций",
                        "Сменить PIN-код",
                        "Информация о банкомате",
                        "Выйти"));

            switch (choice)
            {
                case "Проверить баланс":       await CheckBalanceAsync(); break;
                case "Снять наличные":         await WithdrawAsync(); break;
                case "Внести наличные":        await DepositAsync(); break;
                case "Перевести средства":     await TransferAsync(); break;
                case "История операций":       await ShowHistoryAsync(); break;
                case "Сменить PIN-код":        await ChangePinAsync(); break;
                case "Информация о банкомате": await ShowAtmInfoAsync(); break;
                case "Выйти":
                    _session.Logout();
                    AnsiConsole.MarkupLine("[green]Выход выполнен[/]");
                    await Task.Delay(800);
                    return;
            }
        }
    }

    private async Task CheckBalanceAsync()
    {
        Console.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel($"[green]{account.Balance}[/]")
            .Border(BoxBorder.Rounded)
            .Header("[white]Текущий баланс[/]")
            .HeaderAlignment(Justify.Center));

        var info = new Grid().AddColumn().AddColumn();
        info.AddRow("[grey]Счет:[/]", $"[white]{account.HolderName}[/]");
        info.AddRow("[grey]Карта:[/]", $"[white]{account.CardNumber.Formatted()}[/]");
        info.AddRow("[grey]Дата:[/]", $"[white]{DateTime.Now:dd.MM.yyyy HH:mm}[/]");
        AnsiConsole.Write(info);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Нажмите любую клавишу...[/]");
        Console.ReadKey();
    }

    private async Task WithdrawAsync()
    {
        Console.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]СНЯТИЕ НАЛИЧНЫХ[/]")
            .Border(BoxBorder.Rounded));

        var info = new Grid().AddColumn().AddColumn();
        info.AddRow("[grey]Доступно:[/]", $"[white]{account.Balance}[/]");
        info.AddRow("[grey]Лимит за операцию:[/]", $"[white]{_config.MaxWithdrawalPerTransaction:C}[/]");
        info.AddRow("[grey]Дневной лимит:[/]", $"[white]{_config.MaxWithdrawalPerDay:C}[/]");
        info.AddRow("[grey]Кратно:[/]", $"[white]100 ₽[/]");
        AnsiConsole.Write(info);
        AnsiConsole.WriteLine();

        var amount = AnsiConsole.Prompt(
            new TextPrompt<decimal>("[white]Сумма:[/]")
                .PromptStyle("green")
                .Validate(a => a > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Сумма должна быть больше 0[/]")));

        using var scope = _sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<WithdrawHandler>();
        var result = await handler.HandleAsync(account.Id, _config.DefaultAtmId, amount);

        Console.Clear();

        if (result.IsFailure)
        {
            AnsiConsole.MarkupLine($"[red]{result.Error.Message}[/]");
        }
        else
        {
            account.Debit(Money.Of(amount)); // синхронизируем локальный объект

            var table = new Table().Border(TableBorder.Rounded).Title("[yellow]Выдача купюр[/]");
            table.AddColumn("[white]Номинал[/]");
            table.AddColumn("[white]Кол-во[/]");
            table.AddColumn("[white]Сумма[/]");

            foreach (var (denom, count) in result.Value.Banknotes.OrderByDescending(x => x.Key))
                table.AddRow($"{denom} ₽", $"{count} шт.", $"{denom * count} ₽");

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[green]Снято {amount:C}[/]");
            AnsiConsole.MarkupLine($"[white]Новый баланс:[/] [green]{result.Value.NewBalance}[/]");
            AnsiConsole.MarkupLine("\n[yellow]Заберите деньги и карту[/]");
        }

        await Task.Delay(2500);
    }

    private async Task DepositAsync()
    {
        Console.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]ВНЕСЕНИЕ НАЛИЧНЫХ[/]")
            .Border(BoxBorder.Rounded));

        var amount = AnsiConsole.Prompt(
            new TextPrompt<decimal>("[white]Сумма:[/]")
                .PromptStyle("green")
                .Validate(a => a > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Сумма должна быть больше 0[/]")));

        AnsiConsole.MarkupLine("\n[yellow]Введите количество купюр по номиналам:[/]");

        var banknotes = new Dictionary<int, int>();
        foreach (var denom in Denomination.AllValues.OrderByDescending(x => x))
        {
            var count = AnsiConsole.Prompt(
                new TextPrompt<int>($"[white]Купюр по {denom} ₽:[/]")
                    .PromptStyle("green")
                    .DefaultValue(0));
            if (count > 0) banknotes[denom] = count;
        }

        using var scope = _sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<DepositHandler>();
        var result = await handler.HandleAsync(
            account.Id, _config.DefaultAtmId, amount, banknotes);

        Console.Clear();

        if (result.IsFailure)
        {
            AnsiConsole.MarkupLine($"[red]{result.Error.Message}[/]");
        }
        else
        {
            account.Credit(Money.Of(amount));
            AnsiConsole.MarkupLine($"[green]Внесено {amount:C}[/]");
            AnsiConsole.MarkupLine($"[white]Новый баланс:[/] [green]{result.Value}[/]");
        }

        await Task.Delay(2500);
    }

    private async Task TransferAsync()
    {
        Console.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]ПЕРЕВОД СРЕДСТВ[/]")
            .Border(BoxBorder.Rounded));

        var toCard = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Номер карты получателя:[/]")
                .PromptStyle("green")
                .Validate(c =>
                {
                    var clean = c.Replace(" ", "");
                    if (clean.Length != 16 || !clean.All(char.IsDigit))
                        return ValidationResult.Error("[red]16 цифр[/]");
                    if (clean == account.CardNumber.Value)
                        return ValidationResult.Error("[red]Нельзя перевести себе[/]");
                    return ValidationResult.Success();
                }));

        var amount = AnsiConsole.Prompt(
            new TextPrompt<decimal>("[white]Сумма:[/]")
                .PromptStyle("green")
                .Validate(a => a > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Сумма должна быть больше 0[/]")));

        var confirm = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[white]Перевести {amount:C}?[/]")
                .AddChoices("Да", "Нет"));

        if (confirm == "Нет") return;

        using var scope = _sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<TransferHandler>();
        var result = await handler.HandleAsync(account.Id, _config.DefaultAtmId, toCard, amount);

        Console.Clear();

        if (result.IsFailure)
        {
            AnsiConsole.MarkupLine($"[red]{result.Error.Message}[/]");
        }
        else
        {
            account.Debit(Money.Of(amount));
            AnsiConsole.MarkupLine($"[green]Перевод выполнен[/]");
            AnsiConsole.MarkupLine($"[white]Новый баланс:[/] [green]{result.Value}[/]");
        }

        await Task.Delay(2500);
    }

    private async Task ShowHistoryAsync()
    {
        Console.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]ИСТОРИЯ ОПЕРАЦИЙ[/]")
            .Border(BoxBorder.Rounded)
            .Header("[white]Последние 30 дней[/]"));

        using var scope = _sp.CreateScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<ITransactionRepository>();

        var transactions = await repo.GetByAccountAsync(account.Id, 30);

        if (!transactions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Операций нет[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[white]Дата[/]");
            table.AddColumn("[white]Тип[/]");
            table.AddColumn("[white]Сумма[/]");
            table.AddColumn("[white]Баланс после[/]");
            table.AddColumn("[white]Описание[/]");

            foreach (var t in transactions)
            {
                var typeLabel = t.Type switch
                {
                    TransactionType.Withdraw => "[red]Снятие[/]",
                    TransactionType.Deposit => "[green]Пополнение[/]",
                    TransactionType.TransferOut => "[orange3]Перевод исх.[/]",
                    TransactionType.TransferIn => "[blue]Перевод вх.[/]",
                    TransactionType.BalanceCheck => "[grey]Баланс[/]",
                    TransactionType.Login => "[grey]Вход[/]",
                    _ => t.Type.ToString()
                };

                table.AddRow(
                    t.CreatedAt.ToString("dd.MM.yy HH:mm"),
                    typeLabel,
                    $"{t.Amount}",
                    $"{t.BalanceAfter}",
                    t.Description.Length > 40 ? t.Description[..40] + "..." : t.Description);
            }

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Нажмите любую клавишу...[/]");
        Console.ReadKey();
    }

    private async Task ChangePinAsync()
    {
        Console.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]СМЕНА PIN-КОДА[/]")
            .Border(BoxBorder.Rounded));

        var oldPin = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Текущий PIN:[/]")
                .PromptStyle("red").Secret());

        var newPin = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Новый PIN:[/]")
                .PromptStyle("green").Secret()
                .Validate(p => p.Length == 4 && p.All(char.IsDigit)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]4 цифры[/]")));

        AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Повторите:[/]")
                .PromptStyle("green").Secret()
                .Validate(p => p == newPin
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Не совпадает[/]")));

        using var scope = _sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ChangePinHandler>();
        var result = await handler.HandleAsync(account.Id, oldPin, newPin);

        Console.Clear();
        AnsiConsole.MarkupLine(result.IsSuccess
            ? "[green]PIN изменён[/]"
            : $"[red]{result.Error.Message}[/]");

        await Task.Delay(2000);
    }

    private async Task ShowAtmInfoAsync()
    {
        Console.Clear();

        using var scope = _sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAtmRepository>();
        var atm = await repo.GetByIdAsync(_config.DefaultAtmId);

        if (atm is null)
        {
            AnsiConsole.MarkupLine("[red]Банкомат не найден[/]");
            Console.ReadKey();
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]ИНФОРМАЦИЯ О БАНКОМАТЕ[/]")
            .Border(BoxBorder.Rounded)
            .Header($"[white]{atm.Name}[/]")
            .HeaderAlignment(Justify.Center));

        var info = new Grid().AddColumn().AddColumn();
        info.AddRow("[grey]Адрес:[/]", $"[white]{atm.Location}[/]");
        info.AddRow("[grey]Доступно:[/]", $"[green]{atm.TotalCash()}[/]");
        info.AddRow("[grey]Статус:[/]", atm.IsActive ? "[green]Работает[/]" : "[red]Неактивен[/]");
        info.AddRow("[grey]Обслуживание:[/]", $"[white]{atm.LastMaintenanceAt:dd.MM.yyyy}[/]");
        AnsiConsole.Write(info);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Нажмите любую клавишу...[/]");
        Console.ReadKey();
    }
}