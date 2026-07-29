using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Finance;

[Authorize(Roles = RoleNames.Administrator)]
public class RegisterPaymentModel : PageModel
{
    private static readonly PaymentMethod[] AllowedPaymentMethods = [PaymentMethod.Cash, PaymentMethod.Card];
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public RegisterPaymentModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty]
    public MemberPaymentInputModel Input { get; set; } = new();

    public MemberSummaryViewModel Member { get; private set; } = new();

    public IReadOnlyList<SelectListItem> PlotOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> PaymentMethodOptions { get; private set; } = [];

    public bool HasSinglePlot => PlotOptions.Count == 1;

    public string SinglePlotText => HasSinglePlot ? PlotOptions[0].Text : string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, int? plotId, CancellationToken cancellationToken)
    {
        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (PlotOptions.Count == 0)
        {
            TempData["ErrorMessage"] = "У участника нет активных участков для регистрации платежа.";
            return RedirectToPage("/Administration/Members/Finance", new { id });
        }

        Input.PlotId = plotId.HasValue && PlotOptions.Any(option => option.Value == plotId.Value.ToString())
            ? plotId.Value
            : HasSinglePlot ? int.Parse(PlotOptions[0].Value) : null;
        Input.PaymentDate = DateOnly.FromDateTime(DateTime.Today);
        Input.PaymentMethod = PaymentMethod.Cash;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (PlotOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "У участника нет активных участков для регистрации платежа.");
        }

        if (!Input.PlotId.HasValue && PlotOptions.Count > 0)
        {
            Input.PlotId = int.Parse(PlotOptions[0].Value);
        }

        if (!Input.PaymentMethod.HasValue)
        {
            Input.PaymentMethod = PaymentMethod.Cash;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var validPlotIds = PlotOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!Input.PlotId.HasValue || !validPlotIds.Contains(Input.PlotId.Value.ToString()))
        {
            ModelState.AddModelError(nameof(Input.PlotId), "Выберите участок из списка текущих владений участника.");
            return Page();
        }

        if (!Input.PaymentMethod.HasValue || !AllowedPaymentMethods.Contains(Input.PaymentMethod.Value))
        {
            ModelState.AddModelError(nameof(Input.PaymentMethod), "Выберите допустимый способ оплаты: наличные или банковская карта.");
            return Page();
        }

        var paymentDate = Input.PaymentDate!.Value;
        var memberPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.MemberId == id
                && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= paymentDate)
                && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= paymentDate))
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (!memberPlotIds.Contains(Input.PlotId.Value))
        {
            ModelState.AddModelError(nameof(Input.PlotId), "На дату платежа выбранный участок не принадлежит этому члену товарищества.");
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        var payment = new Payment
        {
            PlotId = Input.PlotId.Value,
            PaymentDate = paymentDate,
            Amount = Input.Amount!.Value,
            PaymentMethod = Input.PaymentMethod!.Value,
            ReferenceNumber = Normalize(Input.ReferenceNumber),
            Description = Normalize(Input.Description),
            CreatedByUserId = currentUser?.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var outstandingCharges = await LoadOutstandingChargesAsync(memberPlotIds, cancellationToken);
        var remainingPaymentAmount = payment.Amount;
        var allocations = new List<PaymentAllocation>();

        foreach (var charge in outstandingCharges)
        {
            if (remainingPaymentAmount <= 0m)
            {
                break;
            }

            var remainingChargeAmount = charge.Amount - charge.AllocatedAmount;
            if (remainingChargeAmount <= 0m)
            {
                continue;
            }

            var allocationAmount = Math.Min(remainingPaymentAmount, remainingChargeAmount);
            allocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                ChargeId = charge.Id,
                Amount = allocationAmount
            });

            remainingPaymentAmount -= allocationAmount;
        }

        if (allocations.Count > 0)
        {
            _dbContext.PaymentAllocations.AddRange(allocations);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = remainingPaymentAmount > 0m
                ? $"Платеж сохранён. Автоматически распределено: {(payment.Amount - remainingPaymentAmount):0.00} грн, аванс: {remainingPaymentAmount:0.00} грн."
                : "Платеж сохранён и автоматически распределён по задолженности участника.";
        }

        return RedirectToPage("/Administration/Members/Finance", new { id });
    }

    private async Task<IReadOnlyList<OutstandingChargeViewModel>> LoadOutstandingChargesAsync(int[] plotIds, CancellationToken cancellationToken)
    {
        if (plotIds.Length == 0)
        {
            return [];
        }

        var charges = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null
                && charge.PlotId.HasValue
                && plotIds.Contains(charge.PlotId.Value))
            .OrderBy(charge => charge.ChargeDate)
            .ThenBy(charge => charge.Id)
            .Select(charge => new OutstandingChargeViewModel
            {
                Id = charge.Id,
                Amount = charge.Amount
            })
            .ToListAsync(cancellationToken);

        if (charges.Count == 0)
        {
            return charges;
        }

        var chargeIds = charges.Select(charge => charge.Id).ToArray();
        var allocatedAmountsByCharge = (await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => chargeIds.Contains(allocation.ChargeId)
                && allocation.Payment != null
                && allocation.Payment.CancelledAtUtc == null)
            .Select(allocation => new
            {
                allocation.ChargeId,
                allocation.Amount
            })
            .ToListAsync(cancellationToken))
            .GroupBy(allocation => allocation.ChargeId)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Amount));

        return charges
            .Select(charge => charge with
            {
                AllocatedAmount = allocatedAmountsByCharge.GetValueOrDefault(charge.Id)
            })
            .ToList();
    }

    private async Task<bool> LoadPageStateAsync(int memberId, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == memberId)
            .Select(item => new MemberSummaryViewModel
            {
                Id = item.Id,
                FullName = item.FullName,
                PhoneNumber = item.PhoneNumber,
                Email = item.Email,
                ActivePlotsCount = item.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return false;
        }

        Member = member;

        PlotOptions = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId, currentDate)
            .OrderBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new SelectListItem
            {
                Value = ownership.PlotId.ToString(),
                Text = ownership.Plot != null
                    ? $"Участок {ownership.Plot.Number}{(string.IsNullOrWhiteSpace(ownership.Plot.Address) ? string.Empty : $" — {ownership.Plot.Address}")}"
                    : $"Участок #{ownership.PlotId}"
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        PaymentMethodOptions = AllowedPaymentMethods
            .Select(method => new SelectListItem
            {
                Value = method.ToString(),
                Text = FinanceDisplayHelper.GetPaymentMethodText(method)
            })
            .ToList();

        return true;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed class MemberSummaryViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string? Email { get; init; }

        public int ActivePlotsCount { get; init; }
    }

    private sealed record OutstandingChargeViewModel
    {
        public long Id { get; init; }

        public decimal Amount { get; init; }

        public decimal AllocatedAmount { get; init; }
    }
}
