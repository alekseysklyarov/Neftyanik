using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public class Member
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(50)]
    [Phone]
    public string? PhoneNumber { get; set; }

    [StringLength(256)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(450)]
    public string? ApplicationUserId { get; set; }

    public ApplicationUser? ApplicationUser { get; set; }

    public MemberElectricityMeterType ElectricityMeterType { get; set; } = MemberElectricityMeterType.SingleRate;

    public bool IsElectricityDisconnected { get; set; }

    public DateOnly? JoinedAt { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public List<PlotOwnership> PlotOwnerships { get; set; } = [];

    public List<MemberElectricityMeter> MemberElectricityMeters { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];

    public List<PaymentNotification> PaymentNotifications { get; set; } = [];
}
