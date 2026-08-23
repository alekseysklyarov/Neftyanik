using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CancelChargeModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IChargeService _chargeService;

    public CancelChargeModel(ApplicationDbContext dbContext, IChargeService chargeService)
    {
        _dbContext = dbContext;
        _chargeService = chargeService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public ChargeCancelViewModel Charge { get; private set; } = new();

    public bool CanCancel => !Charge.IsCancelled;

    public async Task<IActionResult> OnGetAsync(int memberId, long chargeId, CancellationToken cancellationToken)
    {
        var charge = await LoadViewModelAsync(memberId, chargeId, cancellationToken);
        if (charge is null)
        {
            return NotFound();
        }

        Charge = charge;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int memberId, long chargeId, CancellationToken cancellationToken)
    {
        var charge = await LoadViewModelAsync(memberId, chargeId, cancellationToken);
        if (charge is null)
        {
            return NotFound();
        }

        Charge = charge;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _chargeService.CancelChargeAsync(
            new CancelChargeRequest(chargeId, Input.CancellationReason),
            cancellationToken);

        if (!result.Succeeded)
        {
            switch (result.Code)
            {
                case CancelChargeResultCode.AlreadyCancelled:
                    TempData["ErrorMessage"] = "Начисление уже отменено.";
                    return RedirectToPage("/Administration/Members/Finance", new { id = memberId });
                case CancelChargeResultCode.NotFound:
                    return NotFound();
                case CancelChargeResultCode.InvalidCancellationReason:
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.CancellationReason)}", "Укажите причину отмены начисления длиной до 500 символов.");
                    return Page();
                default:
                    ModelState.AddModelError(string.Empty, "Не удалось отменить начисление.");
                    return Page();
            }
        }

        TempData["SuccessMessage"] = "Начисление отменено.";
        return RedirectToPage("/Administration/Members/Finance", new { id = memberId });
    }

    private async Task<ChargeCancelViewModel?> LoadViewModelAsync(int memberId, long chargeId, CancellationToken cancellationToken)
    {
        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == memberId)
            .Select(item => new
            {
                item.Id,
                item.FullName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return null;
        }

        var charge = await _dbContext.Charges
            .AsNoTracking()
            .Where(item => item.Id == chargeId
                && item.PlotId.HasValue
                && _dbContext.PlotOwnerships.Any(ownership => ownership.MemberId == memberId && ownership.PlotId == item.PlotId.Value))
            .Select(item => new ChargeCancelViewModel
            {
                ChargeId = item.Id,
                MemberId = member.Id,
                MemberFullName = member.FullName,
                PlotId = item.PlotId!.Value,
                PlotNumber = item.Plot != null ? item.Plot.Number : "—",
                ChargeTypeName = item.ChargeType != null ? item.ChargeType.Name : "—",
                ChargeDate = item.ChargeDate,
                DueDate = item.DueDate,
                Amount = item.Amount,
                Description = item.Description,
                IsCancelled = item.CancelledAtUtc != null,
                CancelledAtUtc = item.CancelledAtUtc,
                CancellationReason = item.CancellationReason,
                MemberElectricityReadingId = item.MemberElectricityReading != null ? item.MemberElectricityReading.Id : null,
                ReadingDate = item.MemberElectricityReading != null ? item.MemberElectricityReading.ReadingDate : null,
                ReadingAmount = item.MemberElectricityReading != null ? item.MemberElectricityReading.Amount : null,
                AppliedMemberRate = item.MemberElectricityReading != null ? item.MemberElectricityReading.AppliedMemberRate : null,
                AppliedMemberNightRate = item.MemberElectricityReading != null ? item.MemberElectricityReading.AppliedMemberNightRate : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (charge is null)
        {
            return null;
        }

        var allocations = (await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.ChargeId == chargeId)
            .Select(allocation => new ChargeAllocationViewModel
            {
                PaymentId = allocation.PaymentId,
                PaymentDate = allocation.Payment != null ? allocation.Payment.PaymentDate : null,
                PaymentPlotNumber = allocation.Payment != null && allocation.Payment.Plot != null ? allocation.Payment.Plot.Number : null,
                Amount = allocation.Amount,
                IsPaymentCancelled = allocation.Payment != null && allocation.Payment.CancelledAtUtc != null
            })
            .ToListAsync(cancellationToken))
            .OrderBy(allocation => allocation.PaymentDate)
            .ThenBy(allocation => allocation.PaymentId)
            .ToList();

        var activeAllocatedAmount = charge.IsCancelled
            ? 0m
            : allocations.Where(allocation => !allocation.IsPaymentCancelled).Sum(allocation => allocation.Amount);

        return charge with
        {
            ActiveAllocatedAmount = activeAllocatedAmount,
            OutstandingAmount = charge.IsCancelled ? 0m : Math.Max(charge.Amount - activeAllocatedAmount, 0m),
            Allocations = allocations
        };
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите причину отмены начисления.")]
        [StringLength(500, ErrorMessage = "Причина отмены начисления не должна превышать 500 символов.")]
        [Display(Name = "Причина отмены")]
        public string? CancellationReason { get; set; }
    }

    public sealed record ChargeCancelViewModel
    {
        public long ChargeId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string ChargeTypeName { get; init; } = string.Empty;

        public DateOnly ChargeDate { get; init; }

        public DateOnly? DueDate { get; init; }

        public decimal Amount { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public long? MemberElectricityReadingId { get; init; }

        public DateOnly? ReadingDate { get; init; }

        public decimal? ReadingAmount { get; init; }

        public decimal? AppliedMemberRate { get; init; }

        public decimal? AppliedMemberNightRate { get; init; }

        public decimal ActiveAllocatedAmount { get; init; }

        public decimal OutstandingAmount { get; init; }

        public IReadOnlyList<ChargeAllocationViewModel> Allocations { get; init; } = [];
    }

    public sealed class ChargeAllocationViewModel
    {
        public long PaymentId { get; init; }

        public DateOnly? PaymentDate { get; init; }

        public string? PaymentPlotNumber { get; init; }

        public decimal Amount { get; init; }

        public bool IsPaymentCancelled { get; init; }
    }
}
