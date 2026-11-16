using Bankomat.Application.Abstractions;
using Bankomat.Application.Accounts;
using Bankomat.Application.Authentication;
using Bankomat.Application.Operations;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Bankomat.Presentation.Menus;

public class ClientMenu
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ClientMenu>();

    private readonly IServiceScopeFactory _scopes;
    private readonly UserSession _session;
    private readonly IBankConfig _config;

    public ClientMenu(IServiceScopeFactory scopes, UserSession session, IBankConfig config)
    {
        _scopes = scopes;
        _session = session;
        _config = config;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (_session.IsAuthenticated && !ct.IsCancellationRequested)
        {
            Ui.Clear();

            var account = _session.CurrentAccount!;

            AnsiConsole.Write(new Panel($"[yellow]КАРТА: {Ui.E(account.FormattedCard)}[/]")
                .Border(BoxBorder.Rounded)
                .Header($"[white]{Ui.E(account.HolderName)}[/]")
                .HeaderAlignment(Justify.Center));

            AnsiConsole.Write(new Panel($"[green]{Ui.E(account.Balance)}[/]")
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

            Func<Task>? action = choice switch
            {
                "Проверить баланс"       => () => CheckBalanceAsync(ct),
                "Снять наличные"         => () => WithdrawAsync(ct),
                "Внести наличные"        => () => DepositAsync(ct),
                "Перевести средства"     => () => TransferAsync(ct),
                "История операций"       => () => ShowHistoryAsync(ct),
                "Сменить PIN-код"        => () => ChangePinAsync(ct),
                "Информация о банкомате" => () => ShowAtmInfoAsync(ct),
                _ => null
            };

            if (action is null)
            {
                _session.Logout();
                Ui.Success("Выход выполнен");
                await Task.Delay(800, ct);
                return;
            }

            await Ui.GuardAsync(action, Log, choice);
        }
    }

    private async Task CheckBalanceAsync(CancellationToken ct)
    {
        Ui.Clear();

        // Баланс читается из БД: он мог измениться (входящий перевод, другой банкомат)
        using (var scope = _scopes.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<GetAccountSummaryHandler>();
            var fresh = await handler.HandleAsync(_session.CurrentAccount!.Id, ct);
            if (fresh.IsFailure)
            {
                Ui.Error(fresh.Error.Message);
                Ui.WaitKey();
                return;
            }
            _session.Refresh(fresh.Value);
        }

        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel($"[green]{Ui.E(account.Balance)}[/]")
            .Border(BoxBorder.Rounded)
            .Header("[white]Текущий баланс[/]")
            .HeaderAlignment(Justify.Center));

        var info = new Grid().AddColumn().AddColumn();
        info.AddRow("[grey]Владелец:[/]", $"[white]{Ui.E(account.HolderName)}[/]");
        info.AddRow("[grey]Карта:[/]", $"[white]{Ui.E(account.FormattedCard)}[/]");
        info.AddRow("[grey]Дата:[/]", $"[white]{DateTime.Now:dd.MM.yyyy HH:mm}[/]");
        AnsiConsole.Write(info);

        Ui.WaitKey();
    }

    private async Task WithdrawAsync(CancellationToken ct)
    {
        Ui.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]СНЯТИЕ НАЛИЧНЫХ[/]").Border(BoxBorder.Rounded));

        var info = new Grid().AddColumn().AddColumn();
        info.AddRow("[grey]Доступно:[/]", $"[white]{Ui.E(account.Balance)}[/]");
        info.AddRow("[grey]Лимит за операцию:[/]", $"[white]{Ui.E(_config.MaxWithdrawalPerTransaction.ToString("C"))}[/]");
        info.AddRow("[grey]Дневной лимит:[/]", $"[white]{Ui.E(_config.MaxWithdrawalPerDay.ToString("C"))}[/]");
        info.AddRow("[grey]Кратно:[/]", "[white]100 ₽[/]");
        AnsiConsole.Write(info);
        AnsiConsole.WriteLine();

        var amount = AnsiConsole.Prompt(
            new TextPrompt<decimal>("[white]Сумма:[/]")
                .PromptStyle("green")
                .Validate(a => a > 0 && a % 100 == 0
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Сумма должна быть больше 0 и кратна 100[/]")));

        using var scope = _scopes.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<WithdrawHandler>();
        var result = await handler.HandleAsync(account.Id, _config.DefaultAtmId, amount, ct);

        Ui.Clear();

        if (result.IsFailure)
        {
            Ui.Error(result.Error.Message);
        }
        else
        {
            _session.UpdateBalance(result.Value.NewBalance);

            var table = new Table().Border(TableBorder.Rounded).Title("[yellow]Выдача купюр[/]");
            table.AddColumn("[white]Номинал[/]");
            table.AddColumn("[white]Кол-во[/]");
            table.AddColumn("[white]Сумма[/]");

            foreach (var (denom, count) in result.Value.Banknotes.OrderByDescending(x => x.Key))
                table.AddRow($"{denom} ₽", $"{count} шт.", $"{denom * count} ₽");

            AnsiConsole.Write(table);
            Ui.Success($"Снято {amount:C}");
            AnsiConsole.MarkupLine($"[white]Новый баланс:[/] [green]{Ui.E(result.Value.NewBalance)}[/]");
            AnsiConsole.MarkupLine("\n[yellow]Заберите деньги и карту[/]");
        }

        Ui.WaitKey();
    }

    private async Task DepositAsync(CancellationToken ct)
    {
        Ui.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]ВНЕСЕНИЕ НАЛИЧНЫХ[/]").Border(BoxBorder.Rounded));
        AnsiConsole.MarkupLine(
            $"[grey]Лимит за операцию: {Ui.E(_config.MaxDepositPerTransaction.ToString("C"))}[/]");
        AnsiConsole.MarkupLine("\n[yellow]Вставьте купюры (введите количество по номиналам):[/]");

        // Сумму считает банкомат по купюрам — раньше клиент вводил сумму вручную
        // и потом купюры, что давало лишний шанс ошибиться.
        var banknotes = new Dictionary<int, int>();
        foreach (var denom in Denomination.AllValues.OrderByDescending(x => x))
        {
            var count = AnsiConsole.Prompt(
                new TextPrompt<int>($"[white]Купюр по {denom} ₽:[/]")
                    .PromptStyle("green")
                    .DefaultValue(0)
                    .Validate(c => c is >= 0 and <= 10_000
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Введите число от 0 до 10 000[/]")));
            if (count > 0) banknotes[denom] = count;
        }

        if (banknotes.Count == 0)
        {
            Ui.Warning("Купюры не внесены");
            await Task.Delay(1500, ct);
            return;
        }

        var amount = banknotes.Sum(x => (decimal)x.Key * x.Value);
        var confirm = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[white]Зачислить {Ui.E(amount.ToString("C"))}?[/]")
                .AddChoices("Да", "Нет"));
        if (confirm == "Нет") return;

        using var scope = _scopes.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<DepositHandler>();
        var result = await handler.HandleAsync(
            account.Id, _config.DefaultAtmId, amount, banknotes, ct);

        Ui.Clear();

        if (result.IsFailure)
        {
            Ui.Error(result.Error.Message);
            Ui.Warning("Заберите купюры");
        }
        else
        {
            _session.UpdateBalance(result.Value);
            Ui.Success($"Внесено {amount:C}");
            AnsiConsole.MarkupLine($"[white]Новый баланс:[/] [green]{Ui.E(result.Value)}[/]");
        }

        Ui.WaitKey();
    }

    private async Task TransferAsync(CancellationToken ct)
    {
        Ui.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]ПЕРЕВОД СРЕДСТВ[/]").Border(BoxBorder.Rounded));

        var toCard = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Номер карты получателя:[/]")
                .PromptStyle("green")
                .Validate(c =>
                {
                    // Та же нормализация, что и в CardNumber (раньше UI не принимал дефисы)
                    if (!Ui.IsValidCard(c))
                        return ValidationResult.Error("[red]Нужно 16 цифр[/]");
                    if (c.Replace(" ", "").Replace("-", "") == account.CardNumber)
                        return ValidationResult.Error("[red]Нельзя перевести себе[/]");
                    return ValidationResult.Success();
                }));

        var amount = AnsiConsole.Prompt(
            new TextPrompt<decimal>("[white]Сумма:[/]")
                .PromptStyle("green")
                .Validate(a => a > 0 && decimal.Round(a, 2) == a
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Сумма должна быть больше 0, не более 2 знаков после запятой[/]")));

        var confirm = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[white]Перевести {Ui.E(amount.ToString("C"))}?[/]")
                .AddChoices("Да", "Нет"));

        if (confirm == "Нет") return;

        using var scope = _scopes.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<TransferHandler>();
        var result = await handler.HandleAsync(account.Id, _config.DefaultAtmId, toCard, amount, ct);

        Ui.Clear();

        if (result.IsFailure)
        {
            Ui.Error(result.Error.Message);
        }
        else
        {
            _session.UpdateBalance(result.Value);
            Ui.Success("Перевод выполнен");
            AnsiConsole.MarkupLine($"[white]Новый баланс:[/] [green]{Ui.E(result.Value)}[/]");
        }

        Ui.WaitKey();
    }

    private async Task ShowHistoryAsync(CancellationToken ct)
    {
        Ui.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]ИСТОРИЯ ОПЕРАЦИЙ[/]")
            .Border(BoxBorder.Rounded)
            .Header("[white]Последние 30 дней[/]"));

        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();

        var transactions = await repo.GetByAccountAsync(account.Id, days: 30, limit: 50, ct);

        if (transactions.Count == 0)
        {
            Ui.Warning("Операций нет");
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
                    _ => Ui.E(t.Type)
                };

                var description = t.Description.Length > 40 ? t.Description[..40] + "..." : t.Description;

                table.AddRow(
                    // В БД время хранится в UTC — показываем в местном времени
                    Ui.E(t.CreatedAt.ToLocalTime().ToString("dd.MM.yy HH:mm")),
                    typeLabel,
                    Ui.E(t.Amount),
                    Ui.E(t.BalanceAfter),
                    Ui.E(description));
            }

            AnsiConsole.Write(table);
        }

        Ui.WaitKey();
    }

    private async Task ChangePinAsync(CancellationToken ct)
    {
        Ui.Clear();
        var account = _session.CurrentAccount!;

        AnsiConsole.Write(new Panel("[yellow]СМЕНА PIN-КОДА[/]").Border(BoxBorder.Rounded));

        var oldPin = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Текущий PIN:[/]")
                .PromptStyle("red").Secret());

        var newPin = AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Новый PIN:[/]")
                .PromptStyle("green").Secret()
                .Validate(p => p.Length == 4 && p.All(char.IsAsciiDigit)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]4 цифры[/]")));

        AnsiConsole.Prompt(
            new TextPrompt<string>("[white]Повторите:[/]")
                .PromptStyle("green").Secret()
                .Validate(p => p == newPin
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Не совпадает[/]")));

        using var scope = _scopes.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ChangePinHandler>();
        var result = await handler.HandleAsync(account.Id, oldPin, newPin, ct);

        Ui.Clear();
        if (result.IsSuccess)
        {
            Ui.Success("PIN изменён");
            await Task.Delay(2000, ct);
            return;
        }

        Ui.Error(result.Error.Message);

        // Если из-за неверного PIN карту заблокировали — завершаем сессию
        var fresh = await scope.ServiceProvider.GetRequiredService<GetAccountSummaryHandler>()
            .HandleAsync(account.Id, ct);
        if (fresh.IsFailure || fresh.Value.IsBlocked)
        {
            _session.Logout();
            Ui.Warning("Сеанс завершён. Заберите карту.");
        }

        await Task.Delay(2500, ct);
    }

    private async Task ShowAtmInfoAsync(CancellationToken ct)
    {
        Ui.Clear();

        using var scope = _scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAtmRepository>();
        var atm = await repo.GetByIdReadOnlyAsync(_config.DefaultAtmId, ct);

        if (atm is null)
        {
            Ui.Error("Банкомат не найден");
            Ui.WaitKey();
            return;
        }

        AnsiConsole.Write(new Panel("[yellow]ИНФОРМАЦИЯ О БАНКОМАТЕ[/]")
            .Border(BoxBorder.Rounded)
            .Header($"[white]{Ui.E(atm.Name)}[/]")
            .HeaderAlignment(Justify.Center));

        var info = new Grid().AddColumn().AddColumn();
        info.AddRow("[grey]Адрес:[/]", $"[white]{Ui.E(atm.Location)}[/]");
        info.AddRow("[grey]Номиналы:[/]", Ui.E(string.Join(", ",
            atm.Cassettes.Where(c => c.Count > 0).OrderByDescending(c => c.Denomination)
                .Select(c => $"{c.Denomination} ₽"))));
        info.AddRow("[grey]Статус:[/]", atm.IsActive ? "[green]Работает[/]" : "[red]Неактивен[/]");
        info.AddRow("[grey]Обслуживание:[/]", $"[white]{atm.LastMaintenanceAt.ToLocalTime():dd.MM.yyyy}[/]");
        AnsiConsole.Write(info);

        Ui.WaitKey();
    }
}
