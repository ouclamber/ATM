namespace Bankomat.Domain.Atms;

public interface IAtmRepository
{
    /// <summary>Загружает агрегат для изменения (с отслеживанием изменений).</summary>
    Task<Atm?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Загружает агрегат только для чтения (экраны информации, расчёт выдачи).</summary>
    Task<Atm?> GetByIdReadOnlyAsync(int id, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);
    Task AddAsync(Atm atm, CancellationToken ct = default);
}
