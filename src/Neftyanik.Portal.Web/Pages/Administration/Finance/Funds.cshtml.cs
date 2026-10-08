using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class FundsModel(ApplicationDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly From { get; set; } = new(DateTime.Today.Year, 1, 1);
    [BindProperty(SupportsGet = true)] public DateOnly To { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public List<Row> Rows { get; private set; } = [];
    public List<CategoryRow> Categories { get; private set; } = [];
    public decimal UnclassifiedExpenses { get; private set; }
    public async Task OnGetAsync(CancellationToken ct)
    {
        if (From > To) { ModelState.AddModelError(string.Empty, "Начало периода должно быть не позже окончания."); return; }
        var payments = await db.Payments.AsNoTracking().Where(p => p.CancelledAtUtc == null && p.PaymentDate <= To)
            .Select(p => new { p.Id, p.PaymentDate, p.Amount }).ToListAsync(ct);
        var allocations = await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.Payment != null && a.Payment.CancelledAtUtc == null && a.Payment.PaymentDate <= To
                && a.Charge != null && a.Charge.CancelledAtUtc == null)
            .Select(a => new { a.PaymentId, Date = a.Payment!.PaymentDate, a.Amount,
                Fund = a.Charge!.ChargeType != null && a.Charge.ChargeType.Code == ChargeTypeCodes.Electricity ? 2
                    : a.Charge.ChargeType != null && a.Charge.ChargeType.IsMembershipFee ? 1 : 3 }).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => !e.IsCancelled && e.ExpenseDate <= To)
            .Select(e => new { e.ExpenseDate, e.Amount, e.FundingSource, Category = e.ExpenseCategory != null ? e.ExpenseCategory.Name : "—" }).ToListAsync(ct);
        var allocated = allocations.GroupBy(a => a.PaymentId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        for (var fund = 0; fund <= 3; fund++)
        {
            var income = allocations.Where(a => a.Fund == fund).Select(a => (a.Date, a.Amount)).ToList();
            if (fund == 0) income = payments.Select(p => (p.PaymentDate, Math.Max(p.Amount - allocated.GetValueOrDefault(p.Id), 0m))).ToList();
            var spent = expenses.Where(e => e.FundingSource == fund).ToList();
            var opening = income.Where(x => x.Item1 < From).Sum(x => x.Item2) - spent.Where(e => e.ExpenseDate < From).Sum(e => e.Amount);
            Rows.Add(new(fund switch { 1 => "Членские взносы", 2 => "Электроэнергия", 3 => "Прочие средства", _ => "Нераспределённые средства" },
                opening, income.Where(x => x.Item1 >= From).Sum(x => x.Item2), spent.Where(e => e.ExpenseDate >= From).Sum(e => e.Amount)));
        }
        Categories = expenses.Where(e => e.ExpenseDate >= From && e.FundingSource == 1).GroupBy(e => e.Category)
            .Select(g => new CategoryRow(g.Key, g.Sum(e => e.Amount))).OrderBy(r => r.Name).ToList();
        UnclassifiedExpenses = expenses.Where(e => e.FundingSource == 0).Sum(e => e.Amount);
    }
    public sealed record Row(string Name, decimal Opening, decimal Received, decimal Spent)
    { public decimal Closing => Opening + Received - Spent; }
    public sealed record CategoryRow(string Name, decimal Amount);
}
