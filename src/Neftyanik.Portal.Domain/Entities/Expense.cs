namespace Neftyanik.Portal.Domain.Entities;

public class Expense
{
    public long Id { get; set; }

    public int ExpenseCategoryId { get; set; }

    public DateOnly ExpenseDate { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Payee { get; set; }

    public string? DocumentNumber { get; set; }

    public string? AttachmentPath { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public ApplicationUser? CreatedByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsCancelled { get; set; }

    public ExpenseCategory? ExpenseCategory { get; set; }

    public int CategoryId
    {
        get => ExpenseCategoryId;
        set => ExpenseCategoryId = value;
    }

    public ExpenseCategory? Category
    {
        get => ExpenseCategory;
        set => ExpenseCategory = value;
    }

    public DateTime Date
    {
        get => ExpenseDate.ToDateTime(TimeOnly.MinValue);
        set => ExpenseDate = DateOnly.FromDateTime(value);
    }

    public string? PaidById
    {
        get => CreatedByUserId;
        set => CreatedByUserId = value ?? string.Empty;
    }
}
