using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class FinanceModel : PageModel
{
    private const int InitialHistoryCount = 3;
    private const int HistoryBatchSize = 15;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemberElectricityService _memberElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public FinanceModel(ApplicationDbContext dbContext, IMemberElectricityService memberElectricityService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _memberElectricityService = memberElectricityService;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public int ChargePage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PaymentPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    public IReadOnlyList<int> AvailableYears { get; private set; } = [];
    public decimal OpeningBalance { get; private set; }
    public decimal OpeningDebt => Math.Max(OpeningBalance, 0m);
    public decimal OpeningCredit => Math.Max(-OpeningBalance, 0m);

    [BindProperty]
    [ValidateNever]
    public MemberElectricityReadingInputModel ReadingInput { get; set; } = new();

    [BindProperty]
    [ValidateNever]
    public MemberElectricityInitializationInputModel InitializationInput { get; set; } = new();

    [BindProperty]
    [ValidateNever]
    public MemberElectricitySetupInputModel SetupInput { get; set; } = new();

    public MemberFinanceViewModel Member { get; private set; } = new();

    public IReadOnlyList<MemberPlotBalanceViewModel> Plots { get; private set; } = [];

    public IReadOnlyList<ChargeItemViewModel> Charges { get; private set; } = [];

    public IReadOnlyList<PaymentItemViewModel> Payments { get; private set; } = [];

    public long? CorrectablePaymentId { get; private set; }

    public ElectricityFinanceSummary? ElectricitySummary { get; private set; }

    public IReadOnlyDictionary<long, ElectricityChargeDetail> ElectricityCharges { get; private set; }
        = new Dictionary<long, ElectricityChargeDetail>();

    public IReadOnlyDictionary<long, IReadOnlyList<ElectricityPaymentDetail>> ElectricityPayments { get; private set; }
        = new Dictionary<long, IReadOnlyList<ElectricityPaymentDetail>>();

    public IReadOnlyList<MemberElectricityMeterItemViewModel> ElectricityMeters { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ReadingMeterOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> InitializationMeterOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> SetupPlotOptions { get; private set; } = [];

    public MemberElectricityTariffInfoViewModel? CurrentMemberTariff { get; private set; }

    public int ChargeTotalPages { get; private set; } = 1;

    public int PaymentTotalPages { get; private set; } = 1;

    public int ChargeCount { get; private set; }

    public int PaymentCount { get; private set; }

    public int NextChargeCount => Math.Min(HistoryBatchSize, ChargeCount - Charges.Count);

    public int NextPaymentCount => Math.Min(HistoryBatchSize, PaymentCount - Payments.Count);

    public bool HasChargePreviousPage => ChargePage > 1;

    public bool HasChargeNextPage => ChargePage < ChargeTotalPages;

    public bool HasPaymentPreviousPage => PaymentPage > 1;

    public bool HasPaymentNextPage => PaymentPage < PaymentTotalPages;

    public bool HasElectricityMeters => ElectricityMeters.Count > 0;

    public bool CanEnterElectricityReading => ReadingMeterOptions.Count > 0;

    public bool CanInitializeElectricity => InitializationMeterOptions.Count > 0;

    public bool ReopenReadingModal { get; private set; }

    public bool ReopenInitializationModal { get; private set; }

    public bool ReopenSetupModal { get; private set; }

    public bool CanSetupElectricityMeter => !HasElectricityMeters && SetupPlotOptions.Count > 0;

    public bool IsElectricityFeatureAvailable { get; private set; } = true;

    public string? ElectricityFeatureWarningMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        ChargePage = ChargePage < 1 ? 1 : ChargePage;
        PaymentPage = PaymentPage < 1 ? 1 : PaymentPage;

        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        ReadingInput.ReadingDate ??= DateOnly.FromDateTime(DateTime.Today);
        InitializationInput.ReadingDate ??= DateOnly.FromDateTime(DateTime.Today);
        InitializationInput.OpeningDebtAmount ??= 0m;
        SetupInput.ReadingDate ??= DateOnly.FromDateTime(DateTime.Today);
        SetupInput.OpeningDebtAmount ??= 0m;
        SetupInput.BillingPlotId ??= SetupPlotOptions.Count > 0 ? int.Parse(SetupPlotOptions[0].Value) : null;
        ReadingInput.MeterId ??= ReadingMeterOptions.Count > 0 ? int.Parse(ReadingMeterOptions[0].Value) : null;
        InitializationInput.MeterId ??= InitializationMeterOptions.Count > 0 ? int.Parse(InitializationMeterOptions[0].Value) : null;

        return Page();
    }

    public async Task<IActionResult> OnPostCreateElectricityReadingAsync(int id, CancellationToken cancellationToken)
    {
        ChargePage = ChargePage < 1 ? 1 : ChargePage;
        PaymentPage = PaymentPage < 1 ? 1 : PaymentPage;

        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        ModelState.Clear();
        if (!ValidateInputModel(ReadingInput, nameof(ReadingInput)))
        {
            ReopenReadingModal = true;
            return Page();
        }

        if (ReadingMeterOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Нет счётчиков, готовых к вводу показаний.");
        }

        var validMeterIds = ReadingMeterOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (ReadingInput.MeterId.HasValue && !validMeterIds.Contains(ReadingInput.MeterId.Value.ToString()))
        {
            ModelState.AddModelError(nameof(ReadingInput.MeterId), "Выберите счётчик из списка доступных для ввода показаний.");
        }

        if (!ModelState.IsValid)
        {
            ReopenReadingModal = true;
            return Page();
        }

        if (!IsElectricityFeatureAvailable)
        {
            ModelState.AddModelError(string.Empty, ElectricityFeatureWarningMessage ?? "Модуль электросчётчиков недоступен.");
            ReopenInitializationModal = true;
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateReadingAsync(
            new CreateMemberElectricityReadingRequest(
                ReadingInput.MeterId!.Value,
                ReadingInput.ReadingDate!.Value,
                ReadingInput.CurrentReading!.Value,
                ReadingInput.CurrentNightReading,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            AddElectricityOperationError(nameof(ReadingInput), result.ErrorMessage ?? "Не удалось сохранить показания.");
            ReopenReadingModal = true;
            return Page();
        }

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = result.TotalAmount.HasValue
                ? $"Показания сохранены. Начисление по электроэнергии: {result.TotalAmount.Value:0.00} грн."
                : "Показания сохранены.";
        }

        return RedirectToPage("/Administration/Members/Finance", new { id, year = Year, chargePage = ChargePage, paymentPage = PaymentPage });
    }

    public async Task<IActionResult> OnPostSetupElectricityAsync(int id, CancellationToken cancellationToken)
    {
        ChargePage = ChargePage < 1 ? 1 : ChargePage;
        PaymentPage = PaymentPage < 1 ? 1 : PaymentPage;

        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        ModelState.Clear();
        if (!ValidateInputModel(SetupInput, nameof(SetupInput)))
        {
            ReopenSetupModal = true;
            return Page();
        }

        if (SetupPlotOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "У участника нет активных участков для привязки электросчётчика.");
        }

        if (HasElectricityMeters)
        {
            ModelState.AddModelError(string.Empty, "У участника уже есть привязанные электросчётчики. Используйте ввод показаний или инициализацию существующего счётчика.");
        }

        var validPlotIds = SetupPlotOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!SetupInput.BillingPlotId.HasValue || !validPlotIds.Contains(SetupInput.BillingPlotId.Value.ToString()))
        {
            ModelState.AddModelError(nameof(SetupInput.BillingPlotId), "Выберите расчётный участок из текущих участков участника.");
        }

        if (!ModelState.IsValid)
        {
            ReopenSetupModal = true;
            return Page();
        }

        if (!IsElectricityFeatureAvailable)
        {
            ModelState.AddModelError(string.Empty, ElectricityFeatureWarningMessage ?? "Модуль электросчётчиков недоступен.");
            ReopenSetupModal = true;
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateMeterWithInitialReadingAsync(
            new CreateMemberElectricityMeterInitializationRequest(
                id,
                SetupInput.MeterNumber,
                SetupInput.Name,
                true,
                SetupInput.BillingPlotId!.Value,
                [SetupInput.BillingPlotId.Value],
                SetupInput.ReadingDate!.Value,
                SetupInput.CurrentReading!.Value,
                SetupInput.CurrentNightReading,
                SetupInput.OpeningDebtAmount ?? 0m,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось создать электросчётчик и сохранить начальные показания.");
            ReopenSetupModal = true;
            return Page();
        }

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = result.TotalAmount.HasValue && result.TotalAmount.Value > 0m
                ? $"Электросчётчик создан и инициализирован. Начальная задолженность по электроэнергии: {result.TotalAmount.Value:0.00} грн."
                : "Электросчётчик создан и инициализирован.";
        }

        return RedirectToPage("/Administration/Members/Finance", new { id, year = Year, chargePage = ChargePage, paymentPage = PaymentPage });
    }

    public async Task<IActionResult> OnPostInitializeElectricityAsync(int id, CancellationToken cancellationToken)
    {
        ChargePage = ChargePage < 1 ? 1 : ChargePage;
        PaymentPage = PaymentPage < 1 ? 1 : PaymentPage;

        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        ModelState.Clear();
        if (!ValidateInputModel(InitializationInput, nameof(InitializationInput)))
        {
            ReopenInitializationModal = true;
            return Page();
        }

        if (InitializationMeterOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Нет счётчиков, доступных для инициализации.");
        }

        var validMeterIds = InitializationMeterOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (InitializationInput.MeterId.HasValue && !validMeterIds.Contains(InitializationInput.MeterId.Value.ToString()))
        {
            ModelState.AddModelError(nameof(InitializationInput.MeterId), "Выберите счётчик из списка доступных для инициализации.");
        }

        if (!ModelState.IsValid)
        {
            ReopenInitializationModal = true;
            return Page();
        }

        if (!IsElectricityFeatureAvailable)
        {
            ModelState.AddModelError(string.Empty, ElectricityFeatureWarningMessage ?? "Модуль электросчётчиков недоступен.");
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateInitialReadingWithDebtAsync(
            new CreateMemberElectricityInitializationRequest(
                InitializationInput.MeterId!.Value,
                InitializationInput.ReadingDate!.Value,
                InitializationInput.CurrentReading!.Value,
                InitializationInput.CurrentNightReading,
                InitializationInput.OpeningDebtAmount ?? 0m,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            AddElectricityOperationError(nameof(InitializationInput), result.ErrorMessage ?? "Не удалось инициализировать счётчик.");
            ReopenInitializationModal = true;
            return Page();
        }

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = result.TotalAmount.HasValue && result.TotalAmount.Value > 0m
                ? $"Счётчик инициализирован. Начальная задолженность по электроэнергии: {result.TotalAmount.Value:0.00} грн."
                : "Счётчик инициализирован. Начальные показания сохранены.";
        }

        return RedirectToPage("/Administration/Members/Finance", new { id, year = Year, chargePage = ChargePage, paymentPage = PaymentPage });
    }

    private async Task<bool> LoadPageStateAsync(int id, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        if (Year < 1900 || Year > 9998)
        {
            Year = currentDate.Year;
            ModelState.Remove(nameof(Year));
        }
        var yearStart = new DateOnly(Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);

        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MemberFinanceViewModel
            {
                Id = item.Id,
                FullName = item.FullName,
                Email = item.Email,
                PhoneNumber = item.PhoneNumber,
                ElectricityMeterType = item.ElectricityMeterType,
                IsActive = item.IsActive,
                ActivePlotsCount = item.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return false;
        }

        var plotIds = await _dbContext.LoadMemberFinancePlotIdsAsync(id, cancellationToken);

        var chargeDates = await _dbContext.Charges.AsNoTracking()
            .Where(c => c.MemberId == id)
            .Where(c => c.PlotId.HasValue && plotIds.Contains(c.PlotId.Value))
            .Select(c => c.ChargeDate).ToListAsync(cancellationToken);
        var memberPayments = await _dbContext.Payments.AsNoTracking()
            .Where(p => p.MemberId == id)
            .Select(p => new { p.PaymentDate, p.Amount, p.CancelledAtUtc }).ToListAsync(cancellationToken);
        CorrectablePaymentId = await Neftyanik.Portal.Infrastructure.Services.PaymentCorrectionService.LatestActivePaymentIdAsync(_dbContext, id, cancellationToken);
        var recordedYears = chargeDates.Select(d => d.Year).Concat(memberPayments.Select(p => p.PaymentDate.Year))
            .Where(y => y >= 1900 && y <= 9998).Append(currentDate.Year).Append(Year).ToArray();
        AvailableYears = Enumerable.Range(recordedYears.Min(), recordedYears.Max() - recordedYears.Min() + 1)
            .Reverse().ToArray();

        if (plotIds.Length > 0)
        {
            var plots = await _dbContext.Plots
                .AsNoTracking()
                .Where(plot => plotIds.Contains(plot.Id))
                .OrderBy(plot => plot.Number)
                .Select(plot => new
                {
                    plot.Id,
                    plot.Number,
                    plot.Address
                })
                .ToListAsync(cancellationToken);

            var currentPlotIds = await _dbContext.PlotOwnerships.AsNoTracking()
                .WhereCurrentForMember(id, currentDate).Select(o => o.PlotId).ToArrayAsync(cancellationToken);
            SetupPlotOptions = plots.Where(p => currentPlotIds.Contains(p.Id))
                .Select(plot => new SelectListItem
                {
                    Value = plot.Id.ToString(),
                    Text = string.IsNullOrWhiteSpace(plot.Address)
                        ? $"Участок {plot.Number}"
                        : $"Участок {plot.Number} — {plot.Address}"
                })
                .ToList();

            var activeCharges = await _dbContext.Charges
                .AsNoTracking()
            .Where(c => c.MemberId == id)
                .Where(charge => charge.PlotId != null && plotIds.Contains(charge.PlotId.Value) && charge.CancelledAtUtc == null && charge.ChargeDate < yearEnd)
                .Select(charge => new
                {
                    PlotId = charge.PlotId!.Value,
                    charge.Amount,
                    charge.ChargeDate
                })
                .ToListAsync(cancellationToken);
            var chargeTotalsByPlot = activeCharges.Where(c => c.ChargeDate >= yearStart)
                .GroupBy(item => item.PlotId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
            var openingChargesByPlot = activeCharges.Where(c => c.ChargeDate < yearStart)
                .GroupBy(c => c.PlotId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

            var closingPaymentsByPlot = await _dbContext.LoadActivePaymentTotalsByPlotAsync(plotIds, id, cancellationToken, yearEnd);
            var openingPaymentsByPlot = await _dbContext.LoadActivePaymentTotalsByPlotAsync(plotIds, id, cancellationToken, yearStart);

            var totalPayments = memberPayments.Where(p => p.CancelledAtUtc == null && p.PaymentDate >= yearStart && p.PaymentDate < yearEnd).Sum(p => p.Amount);
            OpeningBalance = openingChargesByPlot.Values.Sum()
                - memberPayments.Where(p => p.CancelledAtUtc == null && p.PaymentDate < yearStart).Sum(p => p.Amount);

            Plots = plots
                .Select(plot => new MemberPlotBalanceViewModel
                {
                    PlotId = plot.Id,
                    PlotNumber = plot.Number,
                    Address = plot.Address,
                    Charges = chargeTotalsByPlot.GetValueOrDefault(plot.Id),
                    Payments = closingPaymentsByPlot.GetValueOrDefault(plot.Id) - openingPaymentsByPlot.GetValueOrDefault(plot.Id),
                    OpeningBalance = openingChargesByPlot.GetValueOrDefault(plot.Id) - openingPaymentsByPlot.GetValueOrDefault(plot.Id)
                })
                .ToList();

            Member = member with
            {
                TotalCharges = chargeTotalsByPlot.Values.Sum(),
                TotalPayments = totalPayments,
                OpeningBalance = OpeningBalance
            };

            var chargesQuery = _dbContext.Charges
                .AsNoTracking()
            .Where(c => c.MemberId == id)
                .Where(charge => charge.PlotId != null && plotIds.Contains(charge.PlotId.Value)
                    && charge.ChargeDate >= yearStart && charge.ChargeDate < yearEnd)
                .OrderByDescending(charge => charge.ChargeDate)
                .ThenByDescending(charge => charge.Id);

            ChargeCount = await chargesQuery.CountAsync(cancellationToken);
            ChargeTotalPages = 1 + (int)Math.Ceiling(Math.Max(0, ChargeCount - InitialHistoryCount) / (double)HistoryBatchSize);
            if (ChargePage > ChargeTotalPages)
            {
                ChargePage = ChargeTotalPages;
            }

            Charges = await chargesQuery
                .Take((int)Math.Min(ChargeCount, InitialHistoryCount + (long)(ChargePage - 1) * HistoryBatchSize))
                .Select(charge => new ChargeItemViewModel
                {
                    ChargeId = charge.Id,
                    PlotId = charge.PlotId!.Value,
                    PlotNumber = charge.Plot != null ? charge.Plot.Number : "—",
                    ChargeDate = charge.ChargeDate,
                    ChargeTypeName = charge.ChargeType != null ? charge.ChargeType.Name : "—",
                    Amount = charge.Amount,
                    DueDate = charge.DueDate,
                    Description = charge.Description,
                    IsCancelled = charge.CancelledAtUtc != null,
                    CancelledAtUtc = charge.CancelledAtUtc,
                    CancellationReason = charge.CancellationReason
                })
                .ToListAsync(cancellationToken);

            var paymentsQuery = _dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.MemberId == id
                    && payment.PaymentDate >= yearStart && payment.PaymentDate < yearEnd
                    && payment.PlotId != null)
                .OrderByDescending(payment => payment.PaymentDate)
                .ThenByDescending(payment => payment.Id);

            PaymentCount = await paymentsQuery.CountAsync(cancellationToken);
            PaymentTotalPages = 1 + (int)Math.Ceiling(Math.Max(0, PaymentCount - InitialHistoryCount) / (double)HistoryBatchSize);
            if (PaymentPage > PaymentTotalPages)
            {
                PaymentPage = PaymentTotalPages;
            }

            Payments = await paymentsQuery
                .Take((int)Math.Min(PaymentCount, InitialHistoryCount + (long)(PaymentPage - 1) * HistoryBatchSize))
                .Select(payment => new PaymentItemViewModel
                {
                    PaymentId = payment.Id,
                    PlotId = payment.PlotId!.Value,
                    PlotNumber = payment.Plot != null ? payment.Plot.Number : "—",
                    PaymentDate = payment.PaymentDate,
                    Amount = payment.Amount,
                    PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(payment.PaymentMethod),
                    ReferenceNumber = payment.ReferenceNumber,
                    Description = payment.Description,
                    IsCancelled = payment.CancelledAtUtc != null,
                    CancelledAtUtc = payment.CancelledAtUtc,
                    CancellationReason = payment.CancellationReason
                })
                .ToListAsync(cancellationToken);
        }

        if (plotIds.Length == 0)
        {
            Member = member;
            SetupPlotOptions = [];
        }

        await LoadElectricityStateAsync(id, cancellationToken);

        if (IsElectricityFeatureAvailable)
        {
            ElectricitySummary = await ElectricityFinanceViewData.LoadSummaryAsync(_dbContext, id, plotIds, currentDate, cancellationToken);
            ElectricityCharges = await ElectricityFinanceViewData.LoadChargesAsync(_dbContext, plotIds, Charges.Select(c => c.ChargeId).ToArray(), cancellationToken);
            ElectricityPayments = await ElectricityFinanceViewData.LoadPaymentsAsync(_dbContext, id, Payments.Select(p => p.PaymentId).ToArray(), cancellationToken);
        }

        return true;
    }

    private void AddElectricityOperationError(string propertyPrefix, string errorMessage)
    {
        var targetKey = propertyPrefix switch
        {
            nameof(ReadingInput) => GetReadingErrorKey(propertyPrefix, errorMessage),
            nameof(InitializationInput) => GetInitializationErrorKey(propertyPrefix, errorMessage),
            nameof(SetupInput) => GetSetupErrorKey(propertyPrefix, errorMessage),
            _ => string.Empty
        };

        ModelState.AddModelError(targetKey, errorMessage);
    }

    private static string GetReadingErrorKey(string propertyPrefix, string errorMessage)
    {
        if (errorMessage.Contains("показан", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("изменение", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityReadingInputModel.CurrentReading)}";
        }

        if (errorMessage.Contains("ночн", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityReadingInputModel.CurrentNightReading)}";
        }

        if (errorMessage.Contains("Дата", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityReadingInputModel.ReadingDate)}";
        }

        if (errorMessage.Contains("Счетчик", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("счётчик", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityReadingInputModel.MeterId)}";
        }

        return string.Empty;
    }

    private static string GetInitializationErrorKey(string propertyPrefix, string errorMessage)
    {
        if (errorMessage.Contains("задолж", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityInitializationInputModel.OpeningDebtAmount)}";
        }

        if (errorMessage.Contains("Показание", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityInitializationInputModel.CurrentReading)}";
        }

        if (errorMessage.Contains("ночн", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityInitializationInputModel.CurrentNightReading)}";
        }

        if (errorMessage.Contains("Дата", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityInitializationInputModel.ReadingDate)}";
        }

        if (errorMessage.Contains("Счетчик", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("счётчик", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricityInitializationInputModel.MeterId)}";
        }

        return string.Empty;
    }

    private static string GetSetupErrorKey(string propertyPrefix, string errorMessage)
    {
        if (errorMessage.Contains("задолж", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricitySetupInputModel.OpeningDebtAmount)}";
        }

        if (errorMessage.Contains("Показание", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricitySetupInputModel.CurrentReading)}";
        }

        if (errorMessage.Contains("ночн", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricitySetupInputModel.CurrentNightReading)}";
        }

        if (errorMessage.Contains("участ", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("Расчетный участок", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("Расчётный участок", StringComparison.OrdinalIgnoreCase))
        {
            return $"{propertyPrefix}.{nameof(MemberElectricitySetupInputModel.BillingPlotId)}";
        }

        return string.Empty;
    }

    private bool ValidateInputModel(object model, string prefix)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model);
        var isValid = Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true);

        foreach (var validationResult in validationResults)
        {
            if (validationResult.MemberNames.Any())
            {
                foreach (var memberName in validationResult.MemberNames)
                {
                    ModelState.AddModelError($"{prefix}.{memberName}", validationResult.ErrorMessage ?? "Некорректное значение.");
                }
            }
            else
            {
                ModelState.AddModelError(prefix, validationResult.ErrorMessage ?? "Некорректное значение.");
            }
        }

        return isValid;
    }

    private async Task LoadElectricityStateAsync(int memberId, CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            CurrentMemberTariff = await _dbContext.MemberElectricityTariffs
                .AsNoTracking()
                .Where(item => item.EffectiveFrom <= today)
                .OrderByDescending(item => item.EffectiveFrom)
                .ThenByDescending(item => item.Id)
                .Select(item => new MemberElectricityTariffInfoViewModel
                {
                    EffectiveFrom = item.EffectiveFrom,
                    Rate = item.Rate,
                    NightRate = item.NightRate
                })
                .FirstOrDefaultAsync(cancellationToken);

            var meters = await _dbContext.MemberElectricityMeters
                .AsNoTracking()
                .Where(meter => meter.MemberId == memberId)
                .OrderBy(meter => meter.Name)
                .ThenBy(meter => meter.MeterNumber)
                .Select(meter => new MemberElectricityMeterItemViewModel
                {
                    Id = meter.Id,
                    Name = meter.Name,
                    MeterNumber = meter.MeterNumber,
                    BillingPlotNumber = meter.BillingPlot != null ? meter.BillingPlot.Number : "—",
                    IsActive = meter.IsActive
                })
                .ToListAsync(cancellationToken);

            var meterIds = meters.Select(meter => meter.Id).ToArray();
            var readingsByMeterId = meterIds.Length == 0
                ? new Dictionary<int, List<MemberElectricityReadingHistoryItemViewModel>>()
                : (await _dbContext.MemberElectricityReadings
                    .AsNoTracking()
                    .Where(reading => meterIds.Contains(reading.MemberElectricityMeterId))
                    .OrderBy(reading => reading.ReadingDate)
                    .ThenBy(reading => reading.Id)
                    .Select(reading => new
                    {
                        reading.MemberElectricityMeterId,
                        Item = new MemberElectricityReadingHistoryItemViewModel
                        {
                            Id = reading.Id,
                            ReadingDate = reading.ReadingDate,
                            CurrentReading = reading.CurrentReading,
                            CurrentNightReading = reading.CurrentNightReading,
                            Amount = reading.Amount,
                            IsInitialReading = reading.IsInitialReading
                        }
                    })
                    .ToListAsync(cancellationToken))
                    .GroupBy(item => item.MemberElectricityMeterId)
                    .ToDictionary(
                        group => group.Key,
                        group => BuildReadingHistory(group.Select(item => item.Item).ToList()));

            ElectricityMeters = meters
                .Select(meter =>
                {
                    var readings = readingsByMeterId.GetValueOrDefault(meter.Id, new List<MemberElectricityReadingHistoryItemViewModel>());
                    var latestReading = readings.FirstOrDefault();

                    return meter with
                    {
                        HasReadings = readings.Count > 0,
                        HasInitialReading = readings.Any(reading => reading.IsInitialReading),
                        LatestReadingDate = latestReading?.ReadingDate,
                        LatestReading = latestReading?.CurrentReading,
                        Readings = readings
                    };
                })
                .ToList();

            ReadingMeterOptions = ElectricityMeters
                .Where(meter => meter.IsActive && meter.HasInitialReading)
                .Select(meter => new SelectListItem
                {
                    Value = meter.Id.ToString(),
                    Text = $"{meter.DisplayName} — участок {meter.BillingPlotNumber}"
                })
                .ToList();

            InitializationMeterOptions = ElectricityMeters
                .Where(meter => meter.IsActive && !meter.HasReadings)
                .Select(meter => new SelectListItem
                {
                    Value = meter.Id.ToString(),
                    Text = $"{meter.DisplayName} — участок {meter.BillingPlotNumber}"
                })
                .ToList();

            IsElectricityFeatureAvailable = true;
            ElectricityFeatureWarningMessage = null;
        }
        catch (SqlException exception) when (IsMissingTableException(exception))
        {
            ElectricityMeters = [];
            ReadingMeterOptions = [];
            InitializationMeterOptions = [];
            CurrentMemberTariff = null;
            IsElectricityFeatureAvailable = false;
            ElectricityFeatureWarningMessage = "Модуль электросчётчиков недоступен: необходимо применить обновление базы данных.";
        }
    }

    private static bool IsMissingTableException(SqlException exception)
    {
        return exception.Number == 208
            || exception.Message.Contains("MemberElectricityMeters", StringComparison.OrdinalIgnoreCase);
    }

    private static List<MemberElectricityReadingHistoryItemViewModel> BuildReadingHistory(List<MemberElectricityReadingHistoryItemViewModel> readings)
    {
        decimal? previousReading = null;
        foreach (var reading in readings)
        {
            reading.Consumption = !reading.IsInitialReading && previousReading.HasValue
                ? reading.CurrentReading - previousReading.Value
                : null;

            previousReading = reading.CurrentReading;
        }

        readings.Reverse();
        return readings;
    }

    public sealed record MemberFinanceViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public MemberElectricityMeterType ElectricityMeterType { get; init; } = MemberElectricityMeterType.SingleRate;

        public bool IsActive { get; init; }

        public int ActivePlotsCount { get; init; }

        public decimal TotalCharges { get; init; }

        public decimal TotalPayments { get; init; }

        public decimal OpeningBalance { get; init; }

        public decimal Balance => OpeningBalance + TotalCharges - TotalPayments;

        public decimal BalanceDisplayAmount => Math.Abs(Balance);

        public string BalanceStatusText => FinanceDisplayHelper.GetBalanceStatusText(Balance);

        public string BalanceStatusClass => FinanceDisplayHelper.GetBalanceStatusClass(Balance);

        public string BalanceCardClass => Balance switch
        {
            > 0m => "border-danger",
            < 0m => "border-primary",
            _ => "border-success"
        };
    }

    public sealed class MemberPlotBalanceViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal Charges { get; init; }

        public decimal Payments { get; init; }

        public decimal OpeningBalance { get; init; }

        public decimal Balance => OpeningBalance + Charges - Payments;

        public decimal BalanceDisplayAmount => Math.Abs(Balance);

        public string Status => FinanceDisplayHelper.GetBalanceStatusText(Balance);

        public string BalanceStatusClass => FinanceDisplayHelper.GetBalanceStatusClass(Balance);
    }

    public sealed class MemberElectricityTariffInfoViewModel
    {
        public DateOnly EffectiveFrom { get; init; }

        public decimal Rate { get; init; }

        public decimal? NightRate { get; init; }
    }

    public sealed class ChargeItemViewModel
    {
        public long ChargeId { get; init; }

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public DateOnly ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateOnly? DueDate { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменено", "Скасовано", "Cancelled")
            : AppLocalizer.Get("Активно", "Активно", "Active");
    }

    public sealed class PaymentItemViewModel
    {
        public long PaymentId { get; init; }

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменено", "Скасовано", "Cancelled")
            : AppLocalizer.Get("Активно", "Активно", "Active");
    }

    public sealed record MemberElectricityMeterItemViewModel
    {
        public int Id { get; init; }

        public string? Name { get; init; }

        public string? MeterNumber { get; init; }

        public string BillingPlotNumber { get; init; } = "—";

        public bool IsActive { get; init; }

        public bool HasReadings { get; init; }

        public bool HasInitialReading { get; init; }

        public DateOnly? LatestReadingDate { get; init; }

        public decimal? LatestReading { get; init; }

        public IReadOnlyList<MemberElectricityReadingHistoryItemViewModel> Readings { get; init; } = [];

        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : !string.IsNullOrWhiteSpace(MeterNumber) ? MeterNumber : $"Счётчик #{Id}";
    }

    public sealed class MemberElectricityReadingHistoryItemViewModel
    {
        public long Id { get; init; }
        public DateOnly ReadingDate { get; init; }

        public decimal CurrentReading { get; init; }

        public decimal? CurrentNightReading { get; init; }

        public decimal? Consumption { get; set; }

        public decimal? Amount { get; init; }

        public bool IsInitialReading { get; init; }
    }
}
