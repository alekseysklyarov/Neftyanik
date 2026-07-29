using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MemberListItem> Members { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        var normalizedStatus = NormalizeStatus(Status);
        Status = normalizedStatus;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        var query = _dbContext.Members.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(member =>
                member.FullName.Contains(Search) ||
                (member.PhoneNumber != null && member.PhoneNumber.Contains(Search)) ||
                (member.Email != null && member.Email.Contains(Search)));
        }

        query = normalizedStatus switch
        {
            "archived" => query.Where(member => !member.IsActive),
            "all" => query,
            _ => query.Where(member => member.IsActive)
        };

        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Members = await query
            .OrderBy(member => member.FullName)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(member => new MemberListItem
            {
                Id = member.Id,
                FullName = member.FullName,
                PhoneNumber = member.PhoneNumber,
                JoinedAt = member.JoinedAt,
                IsActive = member.IsActive,
                IsElectricityDisconnected = member.MemberElectricityMeters.Any()
                    && !member.MemberElectricityMeters.Any(meter => meter.IsActive),
                ActiveOwnershipsCount = member.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)),
                Login = member.ApplicationUserId == null
                    ? null
                    : member.ApplicationUser != null
                        ? member.ApplicationUser.UserName
                        : member.ApplicationUserId
            })
            .ToListAsync(cancellationToken);

        if (Members.Count > 0)
        {
            var memberIds = Members.Select(member => member.Id).ToArray();
            var memberPlotPairs = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .WhereCurrentOn(currentDate)
                .Where(ownership => memberIds.Contains(ownership.MemberId))
                .Select(ownership => new
                {
                    ownership.MemberId,
                    ownership.PlotId
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            var plotIds = memberPlotPairs
                .Select(item => item.PlotId)
                .Distinct()
                .ToArray();

            var chargeTotalsByPlot = plotIds.Length == 0
                ? new Dictionary<int, decimal>()
                : (await _dbContext.Charges
                    .AsNoTracking()
                    .Where(charge => charge.PlotId.HasValue && plotIds.Contains(charge.PlotId.Value) && charge.CancelledAtUtc == null)
                    .Select(charge => new
                    {
                        PlotId = charge.PlotId!.Value,
                        charge.Amount
                    })
                    .ToListAsync(cancellationToken))
                    .GroupBy(item => item.PlotId)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

            var paymentTotalsByPlot = plotIds.Length == 0
                ? new Dictionary<int, decimal>()
                : (await _dbContext.Payments
                    .AsNoTracking()
                    .Where(payment => payment.PlotId.HasValue && plotIds.Contains(payment.PlotId.Value) && payment.CancelledAtUtc == null)
                    .Select(payment => new
                    {
                        PlotId = payment.PlotId!.Value,
                        payment.Amount
                    })
                    .ToListAsync(cancellationToken))
                    .GroupBy(item => item.PlotId)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

            var balancesByMember = memberPlotPairs
                .GroupBy(item => item.MemberId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => chargeTotalsByPlot.GetValueOrDefault(item.PlotId) - paymentTotalsByPlot.GetValueOrDefault(item.PlotId)));

            Members = Members
                .Select(member => member with
                {
                    Balance = balancesByMember.GetValueOrDefault(member.Id)
                })
                .ToList();
        }

        EmptyStateMessage = TotalCount == 0 && string.IsNullOrWhiteSpace(Search) && normalizedStatus == "all"
            ? "Члены товарищества пока не добавлены."
            : "По выбранным условиям члены товарищества не найдены.";
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public sealed record MemberListItem
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public DateOnly? JoinedAt { get; init; }

        public bool IsActive { get; init; }

        public bool IsElectricityDisconnected { get; init; }

        public int ActiveOwnershipsCount { get; init; }

        public string? Login { get; init; }

        public decimal Balance { get; init; }

        public string BalanceStatusText => FinanceDisplayHelper.GetBalanceStatusText(Balance);

        public string BalanceStatusClass => FinanceDisplayHelper.GetBalanceStatusClass(Balance);

        public string BalanceDisplayText => Balance == 0m
            ? BalanceStatusText
            : $"{BalanceStatusText}: {Math.Abs(Balance):0.00}";
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "all" => "all",
            "archived" => "archived",
            _ => "active"
        };
    }
}
