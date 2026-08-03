using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class RegisterPaymentModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly UserManager<ApplicationUser> _userManager;

    public RegisterPaymentModel(ApplicationDbContext dbContext, IPaymentService paymentService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _paymentService = paymentService;
        _userManager = userManager;
    }

    [BindProperty]
    public MemberPaymentInputModel Input { get; set; } = new();

    public MemberSummaryViewModel Member { get; private set; } = new();

    public IReadOnlyList<SelectListItem> PlotOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> PaymentMethodOptions { get; private set; } = [];

    public decimal CurrentCashAmount { get; private set; }

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

        if (!Input.PaymentMethod.HasValue || !PaymentMethodRules.IsAllowed(Input.PaymentMethod.Value))
        {
            ModelState.AddModelError(nameof(Input.PaymentMethod), "Выберите допустимый способ оплаты: наличные или перевод на карту.");
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        var paymentResult = await _paymentService.CreateMemberPaymentAsync(
            new CreateMemberPaymentRequest(
                id,
                Input.PlotId.Value,
                Input.PaymentDate!.Value,
                Input.Amount!.Value,
                Input.PaymentMethod!.Value,
                Normalize(Input.ReferenceNumber),
                Normalize(Input.Description),
                currentUser?.Id),
            cancellationToken);

        if (!paymentResult.Succeeded)
        {
            ModelState.AddModelError(
                paymentResult.Code == CreateMemberPaymentResultCode.PaymentPlotNotOwnedByMember ? nameof(Input.PlotId) : string.Empty,
                paymentResult.Code switch
                {
                    CreateMemberPaymentResultCode.PaymentPlotNotOwnedByMember => AppLocalizer.Get(
                        "На дату платежа выбранный участок не принадлежит этому члену товарищества.",
                        "На дату платежу вибрана ділянка не належить цьому члену товариства.",
                        "On the payment date, the selected plot does not belong to this member."),
                    CreateMemberPaymentResultCode.NoEligiblePlots => AppLocalizer.Get(
                        "У участника нет активных участков для регистрации платежа.",
                        "У члена товариства немає активних ділянок для реєстрації платежу.",
                        "The member has no active plots available for payment registration."),
                    CreateMemberPaymentResultCode.InvalidPaymentMethod => AppLocalizer.Get(
                        "Выберите допустимый способ оплаты: наличные или перевод на карту.",
                        "Оберіть допустимий спосіб оплати: готівка або переказ на картку.",
                        "Select a valid payment method: cash or card transfer."),
                    _ => AppLocalizer.Get(
                        "Не удалось зарегистрировать платеж. Проверьте введенные данные и повторите попытку.",
                        "Не вдалося зареєструвати платіж. Перевірте введені дані та повторіть спробу.",
                        "The payment could not be registered. Check the entered data and try again.")
                });

            return Page();
        }

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = paymentResult.AdvanceAmount > 0m
                ? AppLocalizer.Get(
                    $"Платеж сохранён. Автоматически распределено: {paymentResult.AllocatedAmount:0.00} грн, аванс: {paymentResult.AdvanceAmount:0.00} грн.",
                    $"Платіж збережено. Автоматично розподілено: {paymentResult.AllocatedAmount:0.00} грн, аванс: {paymentResult.AdvanceAmount:0.00} грн.",
                    $"The payment has been saved. Automatically allocated: {paymentResult.AllocatedAmount:0.00} UAH, advance: {paymentResult.AdvanceAmount:0.00} UAH.")
                : AppLocalizer.Get(
                    "Платеж сохранён и автоматически распределён по задолженности участника.",
                    "Платіж збережено та автоматично розподілено за заборгованістю члена товариства.",
                    "The payment has been saved and automatically allocated to the member's debt.");
        }

        return RedirectToPage("/Administration/Members/Finance", new { id });
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

        PaymentMethodOptions = PaymentMethodRules.AllowedMethods
            .Select(method => new SelectListItem
            {
                Value = method.ToString(),
                Text = FinanceDisplayHelper.GetPaymentMethodText(method)
            })
            .ToList();

        var cashPayments = (await _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.CancelledAtUtc == null && payment.PaymentMethod == PaymentMethod.Cash)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken))
            .Sum();

        var activeExpenses = (await _dbContext.Expenses
            .AsNoTracking()
            .Where(expense => !expense.IsCancelled)
            .Select(expense => expense.Amount)
            .ToListAsync(cancellationToken))
            .Sum();

        CurrentCashAmount = cashPayments - activeExpenses;

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
}
