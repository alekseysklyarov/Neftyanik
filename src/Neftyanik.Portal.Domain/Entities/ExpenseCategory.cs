namespace Neftyanik.Portal.Domain.Entities;

public class ExpenseCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<Expense> Expenses { get; set; } = [];
}
