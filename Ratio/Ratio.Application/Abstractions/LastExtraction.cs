namespace Ratio.Application.Abstractions;

public enum WarehouseAvailability
{
    Ready,

    Empty,

    Unavailable
}

public readonly record struct LastExtraction(WarehouseAvailability Availability, DateTimeOffset? ExtractedAt)
{
    public static LastExtraction At(DateTimeOffset extractedAt) => new(WarehouseAvailability.Ready, extractedAt);

    public static LastExtraction Empty { get; } = new(WarehouseAvailability.Empty, null);

    public static LastExtraction Unavailable { get; } = new(WarehouseAvailability.Unavailable, null);
}
