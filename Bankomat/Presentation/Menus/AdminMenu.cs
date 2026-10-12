using Bankomat.Application.Abstractions;
using Bankomat.Application.AtmManagement;
using Bankomat.Domain.Atms;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Bankomat.Presentation.Menus;

public class AdminMenu
{
    private readonly IServiceProvider _sp;
    private readonly IBankConfig _config;

    public AdminMenu(IServiceProvider sp, IBankConfig config)
    {
        _sp = sp;
        _config = config;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            Console.Clear();
            AnsiConsole.Write(new Panel("[yellow]УПРАВЛЕНИЕ БАНКОМАТОМ[/]")
                .Border(BoxBorder.Rounded)
                .Header("[white]Административная панель[/]")
                .HeaderAlignment(Justify.Center));

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Выберите действие:[/]")
                    .AddChoices(
                        "Состояние кассет",
                        "Пополнить кассеты",
                        "Расчёт выдачи",
                        "Назад"));

            switch (choice)
            {
                case "Состояние кассет":  await ShowCassettesAsync(); break;
                case "Пополнить кассеты": await RefillAsync(); break;
                case "Расчёт выдачи":     await CalculateAsync(); break;
                case "Назад":             return;
            }
        }
    }

    private async Task<Atm?> LoadAtmAsync()
    {
        using var scope = _sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAtmRepository>();
        return await repo.GetByIdAsync(_config.DefaultAtmId);
    }

    private async Task ShowCassettesAsync()
    {
        Console.Clear();
        var atm = await LoadAtmAsync();
        if (atm is null)
        {
            AnsiConsole.MarkupLine("[red]Банкомат не найден[/]");
            Console.ReadKey();
            return;
        }

        AnsiConsole.Write(new Panel($"[yellow]КАССЕТЫ БАНКОМАТА #{atm.Id}[/]")
            .Border(BoxBorder.Rounded)
            .Header($"[white]{atm.Name}[/]")
            .HeaderAlignment(Justify.Center));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[white]Номинал[/]");
        table.AddColumn("[white]Кол-во[/]");
        table.AddColumn("[white]Макс.[/]");
        table.AddColumn("[white]Сумма[/]");
        table.AddColumn("[white]%[/]");

        foreach (var c in atm.Cassettes.OrderByDescending(x => x.Denomination))
        {
            var pct = (double)c.Count / c.MaxCapacity * 100;
            var color = pct >= 80 ? "green" : pct >= 50 ? "yellow" : pct >= 20 ? "orange3" : "red";

            table.AddRow(
                $"{c.Denomination} ₽",
                $"{c.Count}",
                $"{c.MaxCapacity}",
                $"{c.TotalAmount()}",
                $"[{color}]{pct:F0}%[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[white]Всего:[/] [green]{atm.TotalCash()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Нажмите любую клавишу...[/]");
        Console.ReadKey();
    }

    private async Task RefillAsync()
    {
        Console.Clear();
        var atm = await LoadAtmAsync();
        if (atm is null)
        {
            AnsiConsole.MarkupLine("[red]Банкомат не найден[/]");
            Console.ReadKey();
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]ПОПОЛНЕНИЕ КАССЕТ[/]")
            .Border(BoxBorder.Rounded));

        var banknotes = new Dictionary<int, int>();

        foreach (var denom in Denomination.AllValues.OrderByDescending(x => x))
        {
            var cassette = atm.Cassettes.FirstOrDefault(c => c.Denomination == denom);
            var current = cassette?.Count ?? 0;
            var max = cassette?.MaxCapacity ?? 2000;

            var count = AnsiConsole.Prompt(
                new TextPrompt<int>($"[white]Купюр по {denom} ₽ (есть {current}, макс {max}):[/]")
                    .PromptStyle("green")
                    .DefaultValue(0)
                    .Validate(c =>
                    {
                        if (c < 0)
                            return ValidationResult.Error("[red]Отрицательное число[/]");
                        if (current + c > max)
                            return ValidationResult.Error($"[red]Превышена вместимость ({max})[/]");
                        return ValidationResult.Success();
                    }));

            if (count > 0)
                banknotes[denom] = count;
        }

        if (banknotes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Нечего пополнять[/]");
            await Task.Delay(1500);
            return;
        }

        var totalAmount = banknotes.Sum(x => (decimal)x.Key * x.Value);
        var confirm = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[white]Подтвердить пополнение на {totalAmount:C}?[/]")
                .AddChoices("Да", "Нет"));

        if (confirm == "Нет")
        {
            AnsiConsole.MarkupLine("[yellow]Отменено[/]");
            await Task.Delay(1200);
            return;
        }

        using var scope = _sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RefillHandler>();
        var result = await handler.HandleAsync(_config.DefaultAtmId, banknotes);

        Console.Clear();
        if (result.IsSuccess)
        {
            AnsiConsole.MarkupLine("[green]Кассеты пополнены[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]{result.Error.Message}[/]");
        }
        await Task.Delay(2000);
    }

    private async Task CalculateAsync()
    {
        Console.Clear();
        var atm = await LoadAtmAsync();
        if (atm is null)
        {
            AnsiConsole.MarkupLine("[red]Банкомат не найден[/]");
            Console.ReadKey();
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]РАСЧЁТ ВЫДАЧИ КУПЮР[/]")
            .Border(BoxBorder.Rounded)
            .Header($"[white]Доступно: {atm.TotalCash()}[/]")
            .HeaderAlignment(Justify.Center));

        var amount = AnsiConsole.Prompt(
            new TextPrompt<decimal>("[white]Сумма для расчёта:[/]")
                .PromptStyle("green")
                .Validate(a => a > 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Больше 0[/]")));

        var moneyResult = Money.Create(amount);
        if (moneyResult.IsFailure)
        {
            AnsiConsole.MarkupLine($"[red]{moneyResult.Error.Message}[/]");
            Console.ReadKey();
            return;
        }

        var planResult = atm.PlanWithdrawal(moneyResult.Value);

        Console.Clear();
        if (planResult.IsFailure)
        {
            AnsiConsole.MarkupLine($"[red]{planResult.Error.Message}[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded).Title("[yellow]План выдачи[/]");
            table.AddColumn("[white]Номинал[/]");
            table.AddColumn("[white]Кол-во[/]");
            table.AddColumn("[white]Сумма[/]");

            foreach (var (denom, count) in planResult.Value.Banknotes.OrderByDescending(x => x.Key))
                table.AddRow($"{denom} ₽", $"{count}", $"{denom * count} ₽");

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[green]Итого: {planResult.Value.Total}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Нажмите любую клавишу...[/]");
        Console.ReadKey();
    }
}