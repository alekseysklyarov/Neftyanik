using System.Text.Json;

namespace Neftyanik.Portal.Domain.Entities;

/// <summary>Physical readings whose sums are used by the existing billing meter.</summary>
public sealed record PhysicalMeterReadings(decimal First, decimal Second, decimal? FirstNight, decimal? SecondNight)
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static PhysicalMeterReadings? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PhysicalMeterReadings>(json);
}
