using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Finance;

namespace Neftyanik.Portal.Web.Pages.Finance;

internal static class FinanceCashCalculator
{
    public static async Task<FinanceCashSnapshot> CalculateAsync(ApplicationDbContext dbContext, int currentYear, CancellationToken cancellationToken)
    {
        var currentYearStart = new DateOnly(currentYear, 1, 1);
        var cashInitializationSettingValue = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key == CashInitializationSettingSerializer.SettingKey)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var cashInitialization = CashInitializationSettingSerializer.Deserialize(cashInitializationSettingValue);
        var initializationAmount = cashInitialization?.Amount ?? 0m;
        var advancePaymentsAmount = cashInitialization?.AdvancePaymentsAmount ?? 0m;
        var initializationAcceptedAt = cashInitialization?.AcceptedAt;

        var activePayments = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.CancelledAtUtc == null)
            .Select(payment => new PaymentAmountItem(
                payment.Amount,
                payment.PaymentDate,
                payment.PaymentMethod))
            .ToListAsync(cancellationToken);

        var activeExpenses = await dbContext.Expenses
            .AsNoTracking()
            .Where(expense => !expense.IsCancelled)
            .Select(expense => new ExpenseAmountItem(
                expense.Amount,
                expense.ExpenseDate))
            .ToListAsync(cancellationToken);

        var paymentsFromInitialization = activePayments
            .Where(payment => IsOnOrAfterInitialization(payment.PaymentDate, initializationAcceptedAt))
            .ToList();
        var expensesFromInitialization = activeExpenses
            .Where(expense => IsOnOrAfterInitialization(expense.ExpenseDate, initializationAcceptedAt))
            .ToList();

        var totalPaymentsFromInitialization = paymentsFromInitialization.Sum(payment => payment.Amount);
        var totalCashPaymentsFromInitialization = paymentsFromInitialization
            .Where(payment => payment.PaymentMethod == PaymentMethod.Cash)
            .Sum(payment => payment.Amount);
        var totalNonCashPaymentsFromInitialization = paymentsFromInitialization
            .Where(payment => payment.PaymentMethod != PaymentMethod.Cash)
            .Sum(payment => payment.Amount);
        var totalExpensesFromInitialization = expensesFromInitialization.Sum(expense => expense.Amount);

        var openingYearInitializationAmount = cashInitialization is not null && cashInitialization.AcceptedAt < currentYearStart
            ? cashInitialization.Amount - cashInitialization.AdvancePaymentsAmount
            : 0m;
        var openingYearPayments = activePayments
            .Where(payment => payment.PaymentDate < currentYearStart
                && IsOnOrAfterInitialization(payment.PaymentDate, initializationAcceptedAt))
            .Sum(payment => payment.Amount);
        var openingYearExpenses = activeExpenses
            .Where(expense => expense.ExpenseDate < currentYearStart
                && IsOnOrAfterInitialization(expense.ExpenseDate, initializationAcceptedAt))
            .Sum(expense => expense.Amount);

        return new FinanceCashSnapshot(
            initializationAmount - advancePaymentsAmount + totalPaymentsFromInitialization - totalExpensesFromInitialization,
            initializationAmount - advancePaymentsAmount + totalCashPaymentsFromInitialization - totalExpensesFromInitialization,
            totalNonCashPaymentsFromInitialization,
            openingYearInitializationAmount + openingYearPayments - openingYearExpenses,
            advancePaymentsAmount,
            initializationAcceptedAt);
    }

    private static bool IsOnOrAfterInitialization(DateOnly value, DateOnly? initializationAcceptedAt)
    {
        return initializationAcceptedAt is null || value >= initializationAcceptedAt.Value;
    }

    internal sealed record FinanceCashSnapshot(
        decimal CurrentCashAmount,
        decimal CurrentCashOnlyAmount,
        decimal CurrentNonCashAmount,
        decimal OpeningYearCashAmount,
        decimal AdvancePaymentsAmount,
        DateOnly? InitializationAcceptedAt);

    private sealed record PaymentAmountItem(decimal Amount, DateOnly PaymentDate, PaymentMethod PaymentMethod);

    private sealed record ExpenseAmountItem(decimal Amount, DateOnly ExpenseDate);
}
