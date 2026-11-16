using Spectre.Console;

namespace Bankomat.Presentation;

/// <summary>Общие помощники консольного UI.</summary>
internal static class Ui
{
    /// <summary>Экранирует пользовательские/доменные строки для разметки Spectre.Console.</summary>
    public static string E(object? value) => Markup.Escape(value?.ToString() ?? string.Empty);

    public static void Error(string message) =>
        AnsiConsole.MarkupLine($"[red]{E(message)}[/]");

    public static void Success(string message) =>
        AnsiConsole.MarkupLine($"[green]{E(message)}[/]");

    public static void Warning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]{E(message)}[/]");

    /// <summary>Ожидание нажатия клавиши, безопасное при перенаправленном вводе (CI, Docker без -it).</summary>
    public static void WaitKey(string text = "Нажмите любую клавишу...")
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]{E(text)}[/]");
        if (Console.IsInputRedirected)
            Console.ReadLine();
        else
            Console.ReadKey(intercept: true);
    }

    public static void Clear()
    {
        if (!Console.IsOutputRedirected)
            AnsiConsole.Clear();
    }

    /// <summary>
    /// Изолирует сбой одного действия (например, БД временно недоступна) —
    /// раньше любое исключение в меню завершало всё приложение.
    /// </summary>
    public static async Task GuardAsync(Func<Task> action, Serilog.ILogger log, string actionName)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error(ex, "Ошибка при выполнении действия {Action}", actionName);
            Clear();
            Error("Произошла техническая ошибка. Операция не выполнена, попробуйте ещё раз.");
            Error($"Детали: {ex.GetBaseException().Message}");
            WaitKey();
        }
    }

    public static bool IsValidCard(string input)
    {
        var clean = input.Replace(" ", "").Replace("-", "");
        return clean.Length == 16 && clean.All(char.IsAsciiDigit);
    }
}
