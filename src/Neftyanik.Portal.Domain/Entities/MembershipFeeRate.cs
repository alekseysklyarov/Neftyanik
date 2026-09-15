namespace Neftyanik.Portal.Domain.Entities;

public class MembershipFeeRate : IAssociationOwned
{
    public int AssociationId { get; set; }

    public Association? Association { get; set; }

    public int Id { get; set; }

    public int Year { get; set; }

    public decimal AmountPerPlot { get; set; }

    public DateOnly? DueDate { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
