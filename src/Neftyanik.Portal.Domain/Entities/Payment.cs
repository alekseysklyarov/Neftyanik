using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public enum PaymentSource
{
    MembershipFee = 0,
    ElectricityCharge = 1,
    Expense = 2,
    Other = 10
}

public class Payment
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateOnly PaymentDate { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Comment { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsCancelled { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public string? CancelledByUserId { get; set; }

    // User navigations are kept in Infrastructure to avoid domain->infrastructure dependency
    public List<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();

    public PaymentSource Source { get; set; } = PaymentSource.Other;

    public string PayerId
    {
        get => UserId;
        set => UserId = value ?? string.Empty;
    }

    public DateTime Date
    {
        get => PaymentDate.ToDateTime(TimeOnly.MinValue);
        set => PaymentDate = DateOnly.FromDateTime(value);
    }

    public string? Method
    {
        get => PaymentMethod.ToString();
        set
        {
            if (Enum.TryParse<PaymentMethod>(value, true, out var parsedMethod))
            {
                PaymentMethod = parsedMethod;
            }
        }
    }

    public string? Note
    {
        get => Comment;
        set => Comment = value;
    }
}
