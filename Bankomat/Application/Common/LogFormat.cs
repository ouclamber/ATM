namespace Bankomat.Application.Common;

/// <summary>Безопасное форматирование данных для логов.</summary>
public static class LogFormat
{
    /// <summary>Маскирует номер карты: в логи попадают только последние 4 цифры.</summary>
    public static string MaskCard(string? card)
    {
        if (string.IsNullOrEmpty(card)) return "****";
        var clean = card.Replace(" ", "").Replace("-", "");
        return clean.Length >= 4 ? $"**** **** **** {clean[^4..]}" : "****";
    }

    public static string Banknotes(IReadOnlyDictionary<int, int> banknotes) =>
        "[" + string.Join(", ", banknotes
            .Where(x => x.Value != 0)
            .OrderByDescending(x => x.Key)
            .Select(x => $"{x.Key}×{x.Value}")) + "]";
}
