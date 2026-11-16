using Bankomat.Application.Abstractions;
using Bankomat.Application.AtmManagement;
using Bankomat.Domain.Atms;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Bankomat.Presentation.Menus;

public class AdminMenu
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<AdminMenu>();

    private readonly IServiceScopeFactory _scopes;
    private readonly IBankConfig _config;

    public AdminMenu(IServiceScopeFactory scopes, IBankConfig config)
    {
        _scopes = scopes;
        _config = config;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            Ui.Clear();
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

            Func<Task>? action = choice switch
            {
                "Состояние кассет"  => () => ShowCassettesAsync(ct),
                "Пополнить кассеты" => () => RefillAsync(ct),
                "Расчёт выдачи"     => () => CalculateAsync(ct),
                _ => null
            };

            if (action is null)
                return;

            await Ui.GuardAsync(action, Log, choice);
        }
    }

    private async Task<Atm?> LoadAtmAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAtmRepository>();
        return await repo.GetByIdReadOnlyAsync(_config.DefaultAtmId, ct);
    }

    private async Task ShowCassettesAsync(CancellationToken ct)
    {
        Ui.Clear();
        var atm = await LoadAtmAsync(ct);
        if (atm is null)
        {
            Ui.Error("Банкомат не найден");
            Ui.WaitKey();
            return;
        }

        AnsiConsole.Write(new Panel($"[yellow]КАССЕТЫ БАНКОМАТА #{atm.Id}[/]")
            .Border(BoxBorder.Rounded)
            .Header($"[white]{Ui.E(atm.Name)}[/]")
            .HeaderAlignment(Justify.Center));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[white]Номинал[/]");
        table.AddColumn("[white]Кол-во[/]");
        table.AddColumn("[white]Макс.[/]");
        table.AddColumn("[white]Сумма[/]");
        table.AddColumn("[white]%[/]");

        foreach (var c in atm.Cassettes.OrderByDescending(x => x.Denomination))
        {
            var pct = c.MaxCapacity == 0 ? 0 : (double)c.Count / c.MaxCapacity * 100;
            var color = pct >= 80 ? "green" : pct >= 50 ? "yellow" : pct >= 20 ? "orange3" : "red";

            table.AddRow(
                $"{c.Denomination} ₽",
                $"{c.Count}",
                $"{c.MaxCapacity}",
                Ui.E(c.TotalAmount()),
                $"[{color}]{pct:F0}%[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[white]Всего:[/] [green]{Ui.E(atm.TotalCash())}[/]");
        Ui.WaitKey();
    }

    private async Task RefillAsync(CancellationToken ct)
    {
        Ui.Clear();
        var atm = await LoadAtmAsync(ct);
        if (atm is null)
        {
            Ui.Error("Банкомат не найден");
            Ui.WaitKey();
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]ПОПОЛНЕНИЕ КАССЕТ[/]")
            .Border(BoxBorder.Rounded));

        var banknotes = new Dictionary<int, int>();

        // Только существующие кассеты (раньше предлагался номинал без кассеты с «макс 2000»,
        // а потом домен всё равно отклонял операцию)
        foreach (var cassette in atm.Cassettes.OrderByDescending(c => c.Denomination))
        {
            var denom = cassette.Denomination;
            var current = cassette.Count;
            var max = cassette.MaxCapacity;

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
            Ui.Warning("Нечего пополнять");
            await Task.Delay(1500, ct);
            return;
        }

        var totalAmount = banknotes.Sum(x => (decimal)x.Key * x.Value);
        var confirm = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[white]Подтвердить пополнение на {Ui.E(totalAmount.ToString("C"))}?[/]")
                .AddChoices("Да", "Нет"));

        if (confirm == "Нет")
        {
            Ui.Warning("Отменено");
            await Task.Delay(1200, ct);
            return;
        }

        using var scope = _scopes.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RefillHandler>();
        var result = await handler.HandleAsync(_config.DefaultAtmId, banknotes, ct);

        Ui.Clear();
        if (result.IsSuccess)
        {
            AnsiConsole.MarkupLine("[green]Кассеты пополнены[/]");
        }
        else
        {
            Ui.Error(result.Error.Message);
        }
        await Task.Delay(2000, ct);
    }

    private async Task CalculateAsync(CancellationToken ct)
    {
        Ui.Clear();
        var atm = await LoadAtmAsync(ct);
        if (atm is null)
        {
            Ui.Error("Банкомат не найден");
            Ui.WaitKey();
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]РАСЧЁТ ВЫДАЧИ КУПЮР[/]")
            .Border(BoxBorder.Rounded)
            .Header($"[white]Доступно: {Ui.E(atm.TotalCash())}[/]")
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
            Ui.Error(moneyResult.Error.Message);
            Ui.WaitKey();
            return;
        }

        var planResult = atm.PlanWithdrawal(moneyResult.Value);

        Ui.Clear();
        if (planResult.IsFailure)
        {
            Ui.Error(planResult.Error.Message);
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
            Ui.Success($"Итого: {planResult.Value.Total}");
        }

        Ui.WaitKey();
    }
}