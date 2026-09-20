namespace Ratio.Application.Abstractions;

/// <summary>Por que o DW não está pronto — ou que ele está.</summary>
public enum WarehouseAvailability
{
    /// <summary>Há carga publicada e utilizável.</summary>
    Ready,

    /// <summary>O banco respondeu, mas não há carga publicada (schema ou fato vazio).</summary>
    Empty,

    /// <summary>Não foi possível consultar o banco. A causa vai no log.</summary>
    Unavailable
}

/// <summary>
/// Resultado da leitura da última extração. Existe para que "base vazia" e "banco
/// inacessível" não cheguem à tela como a mesma coisa: quem opera o servidor do
/// cliente precisa saber se falta restaurar o dump ou se falta consertar o banco.
/// </summary>
public readonly record struct LastExtraction(WarehouseAvailability Availability, DateTimeOffset? ExtractedAt)
{
    public static LastExtraction At(DateTimeOffset extractedAt) => new(WarehouseAvailability.Ready, extractedAt);

    public static LastExtraction Empty { get; } = new(WarehouseAvailability.Empty, null);

    public static LastExtraction Unavailable { get; } = new(WarehouseAvailability.Unavailable, null);
}
