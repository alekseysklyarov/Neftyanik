using Neftyanik.Portal.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Domain.Entities;

public class PaymentNotification : IValidatableObject
{
    public const int DescriptionMaxLength = 1000;
    public const int AdministratorCommentMaxLength = 1000;

    public long Id { get; set; }

    public int MemberId { get; set; }

    public Member? Member { get; set; }

    public decimal Amount { get; set; }

    public Neftyanik.Portal.Domain.Enums.PaymentMethod PaymentMethod { get; set; }

    public string? Description { get; set; }

    public Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus Status { get; set; } = Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus.Pending;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public string? ReviewedByUserId { get; set; }

    public ApplicationUser? ReviewedByUser { get; set; }

    public string? AdministratorComment { get; set; }

    public long? PaymentId { get; set; }

    public Payment? Payment { get; set; }

    public int ReviewVersion { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
        {
            yield return new ValidationResult("Payment notification amount must be greater than zero.", [nameof(Amount)]);
        }

        if (!string.IsNullOrWhiteSpace(Description) && Description.Trim().Length > DescriptionMaxLength)
        {
            yield return new ValidationResult($"Payment notification description must not exceed {DescriptionMaxLength} characters.", [nameof(Description)]);
        }

        if (!string.IsNullOrWhiteSpace(AdministratorComment) && AdministratorComment.Trim().Length > AdministratorCommentMaxLength)
        {
            yield return new ValidationResult($"Administrator comment must not exceed {AdministratorCommentMaxLength} characters.", [nameof(AdministratorComment)]);
        }

        if (!Enum.IsDefined(PaymentMethod))
        {
            yield return new ValidationResult("Payment notification method is invalid.", [nameof(PaymentMethod)]);
        }

        if (Status != Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus.Pending && !ReviewedAtUtc.HasValue)
        {
            yield return new ValidationResult("Reviewed payment notifications must have a review timestamp.", [nameof(ReviewedAtUtc)]);
        }

        if (Status != Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus.Pending && string.IsNullOrWhiteSpace(ReviewedByUserId))
        {
            yield return new ValidationResult("Reviewed payment notifications must store the reviewing user.", [nameof(ReviewedByUserId)]);
        }

        if (Status == Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus.Confirmed && !PaymentId.HasValue)
        {
            yield return new ValidationResult("Confirmed payment notifications must reference a registered payment.", [nameof(PaymentId)]);
        }

        if (Status == Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus.Rejected && PaymentId.HasValue)
        {
            yield return new ValidationResult("Rejected payment notifications cannot reference a registered payment.", [nameof(PaymentId)]);
        }
    }
}
