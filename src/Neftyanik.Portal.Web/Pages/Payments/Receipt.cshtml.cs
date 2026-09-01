using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Payments;

[Authorize(Roles = RoleNames.Administrator + "," + RoleNames.Accountant + "," + RoleNames.Member)]
public class ReceiptModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReceiptModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public PaymentReceiptViewModel Receipt { get; private set; } = new();

    public bool IsMemberView => User.IsInRole(RoleNames.Member);

    public DateTime GeneratedAtUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync(long paymentId, int? memberId, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments
            .AsNoTracking()
            .Where(item => item.Id == paymentId)
            .Select(item => new
            {
                item.Id,
                item.PlotId,
                item.PaymentDate,
                item.Amount,
                item.PaymentMethod,
                item.ReferenceNumber,
                item.Description,
                item.CreatedAtUtc,
                item.CancelledAtUtc,
                item.CancellationReason,
                PlotNumber = item.Plot != null ? item.Plot.Number : "—",
                PlotAddress = item.Plot != null ? item.Plot.Address : null,
                CreatedByDisplayName = item.CreatedByUser != null ? item.CreatedByUser.DisplayName : null,
                CreatedByFirstName = item.CreatedByUser != null ? item.CreatedByUser.FirstName : null,
                CreatedByLastName = item.CreatedByUser != null ? item.CreatedByUser.LastName : null,
                CreatedByUserName = item.CreatedByUser != null ? item.CreatedByUser.UserName : null,
                HasPaymentNotification = item.PaymentNotification != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null || !payment.PlotId.HasValue)
        {
            return NotFound();
        }

        int resolvedMemberId;
        if (User.IsInRole(RoleNames.Member))
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            if (user.MustChangePassword)
            {
                return RedirectToPage("/Account/ChangeInitialPassword");
            }

            var currentMemberId = await _dbContext.Members
                .AsNoTracking()
                .Where(item => item.ApplicationUserId == user.Id)
                .Select(item => (int?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!currentMemberId.HasValue)
            {
                return NotFound();
            }

            resolvedMemberId = currentMemberId.Value;
        }
        else if (User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Accountant))
        {
            if (!memberId.HasValue)
            {
                return NotFound();
            }

            resolvedMemberId = memberId.Value;
        }
        else
        {
            return Forbid();
        }

        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var canViewPayment = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(resolvedMemberId, currentDate)
            .AnyAsync(ownership => ownership.PlotId == payment.PlotId.Value, cancellationToken);

        if (!canViewPayment)
        {
            return NotFound();
        }

        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == resolvedMemberId)
            .Select(item => new
            {
                item.Id,
                item.FullName,
                item.PhoneNumber,
                item.Email
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        var allocations = await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PaymentId == paymentId)
            .Select(allocation => new PaymentAllocationReceiptViewModel
            {
                ChargeId = allocation.ChargeId,
                ChargeDate = allocation.Charge != null ? allocation.Charge.ChargeDate : null,
                ChargeTypeName = allocation.Charge != null && allocation.Charge.ChargeType != null
                    ? allocation.Charge.ChargeType.Name
                    : "—",
                ChargeDescription = allocation.Charge != null ? allocation.Charge.Description : null,
                ChargeAmount = allocation.Charge != null ? allocation.Charge.Amount : null,
                AllocatedAmount = allocation.Amount,
                IsChargeCancelled = allocation.Charge != null && allocation.Charge.CancelledAtUtc != null
            })
            .ToListAsync(cancellationToken);

        allocations = allocations
            .OrderBy(item => item.ChargeDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.ChargeId)
            .ToList();

        Receipt = new PaymentReceiptViewModel
        {
            PaymentId = payment.Id,
            MemberId = member.Id,
            MemberFullName = member.FullName,
            MemberPhoneNumber = member.PhoneNumber,
            MemberEmail = member.Email,
            PlotId = payment.PlotId.Value,
            PlotNumber = payment.PlotNumber,
            PlotAddress = payment.PlotAddress,
            PaymentDate = payment.PaymentDate,
            Amount = payment.Amount,
            PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(payment.PaymentMethod),
            ReferenceNumber = payment.ReferenceNumber,
            Description = payment.Description,
            RegisteredAtUtc = payment.CreatedAtUtc,
            RegisteredBy = BuildUserDisplayName(
                payment.CreatedByDisplayName,
                payment.CreatedByFirstName,
                payment.CreatedByLastName,
                payment.CreatedByUserName),
            IsCancelled = payment.CancelledAtUtc.HasValue,
            CancelledAtUtc = payment.CancelledAtUtc,
            CancellationReason = payment.CancellationReason,
            HasPaymentNotification = payment.HasPaymentNotification,
            Allocations = allocations
        };

        GeneratedAtUtc = DateTime.UtcNow;
        return Page();
    }

    private static string BuildUserDisplayName(string? displayName, string? firstName, string? lastName, string? userName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        var fullName = string.Join(' ', new[] { firstName, lastName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        return string.IsNullOrWhiteSpace(userName)
            ? AppLocalizer.Get("Не указан", "Не вказано", "Not specified")
            : userName;
    }

    public sealed class PaymentReceiptViewModel
    {
        public long PaymentId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public string? MemberPhoneNumber { get; init; }

        public string? MemberEmail { get; init; }

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public DateTime RegisteredAtUtc { get; init; }

        public string RegisteredBy { get; init; } = string.Empty;

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public bool HasPaymentNotification { get; init; }

        public IReadOnlyList<PaymentAllocationReceiptViewModel> Allocations { get; init; } = [];

        public decimal AllocatedTotal => Allocations.Sum(item => item.AllocatedAmount);

        public decimal UnallocatedAmount => Math.Max(0m, Amount - AllocatedTotal);

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменён", "Скасований", "Cancelled")
            : AppLocalizer.Get("Зарегистрирован", "Зареєстрований", "Registered");
    }

    public sealed class PaymentAllocationReceiptViewModel
    {
        public long ChargeId { get; init; }

        public DateOnly? ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public string? ChargeDescription { get; init; }

        public decimal? ChargeAmount { get; init; }

        public decimal AllocatedAmount { get; init; }

        public bool IsChargeCancelled { get; init; }
    }
}
