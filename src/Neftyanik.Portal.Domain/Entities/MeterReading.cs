using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public class MeterReading
{
    public long Id { get; set; }

    public int MeterId { get; set; }

    public DateOnly ReadingDate { get; set; }

    public decimal? TotalValue { get; set; }

    public decimal? DayValue { get; set; }

    public decimal? NightValue { get; set; }

    public string? SubmittedByUserId { get; set; }

    public ApplicationUser? SubmittedByUser { get; set; }

    public MeterReadingStatus Status { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ApprovedByUserId { get; set; }

    public ApplicationUser? ApprovedByUser { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? Comment { get; set; }

    public string? MeterPhotoPath { get; set; }

    public ElectricityMeter? Meter { get; set; }
}