using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Enums;
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
    public string Status { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "fullname";

    [BindProperty(SupportsGet = true)]
    public string SortDirection { get; set; } = "asc";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MemberListItem> Members { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        NormalizeFilterState();

        var query = _dbContext.Members.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(member =>
                member.FullName.Contains(Search) ||
                (member.PhoneNumber != null && member.PhoneNumber.Contains(Search)) ||
                (member.Email != null && member.Email.Contains(Search)));
        }

        query = Status switch
        {
            "archived" => query.Where(member => !member.IsActive),
            "all" => query,
            _ => query.Where(member => member.IsActive)
        };

        var members = await query
            .Select(member => new MemberListItem
            {
                Id = member.Id,
                FullName = member.FullName,
                PhoneNumber = member.PhoneNumber,
                ElectricityMeterType = member.ElectricityMeterType,
                IsElectricityDisconnected = member.IsElectricityDisconnected,
                JoinedAt = member.JoinedAt,
                IsActive = member.IsActive,
                Login = member.ApplicationUserId == null
                    ? null
                    : member.ApplicationUser != null
                        ? member.ApplicationUser.UserName
                        : member.ApplicationUserId
            })
            .ToListAsync(cancellationToken);

        if (members.Count > 0)
        {
            var memberIds = members.Select(member => member.Id).ToArray();
            var currentOwnerships = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .WhereCurrentOn(currentDate)
                .Where(ownership => memberIds.Contains(ownership.MemberId))
                .Select(ownership => new
                {
                    ownership.MemberId,
                    ownership.PlotId
                })
                .ToListAsync(cancellationToken);

            var activeOwnershipsCountByMember = currentOwnerships
                .GroupBy(item => item.MemberId)
                .ToDictionary(group => group.Key, group => group.Count());

            var memberPlotPairs = currentOwnerships
                .Distinct()
                .ToList();

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

            foreach (var member in members)
            {
                member.ActiveOwnershipsCount = activeOwnershipsCountByMember.GetValueOrDefault(member.Id);
                member.Balance = balancesByMember.GetValueOrDefault(member.Id);
            }
        }

        TotalCount = members.Count;
        TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Members = ApplySorting(members)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        EmptyStateMessage = TotalCount == 0 && string.IsNullOrWhiteSpace(Search) && Status == "all"
            ? "Члены товарищества пока не добавлены."
            : "По выбранным условиям члены товарищества не найдены.";
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public string GetNextSortDirection(string sortBy)
    {
        return string.Equals(SortBy, sortBy, StringComparison.OrdinalIgnoreCase) && SortDirection == "asc"
            ? "desc"
            : "asc";
    }

    public string GetSortIndicator(string sortBy)
    {
        if (!string.Equals(SortBy, sortBy, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return SortDirection == "desc" ? "↓" : "↑";
    }

    public sealed class MemberListItem
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public MemberElectricityMeterType ElectricityMeterType { get; init; }

        public DateOnly? JoinedAt { get; init; }

        public bool IsActive { get; init; }

        public bool IsElectricityDisconnected { get; init; }

        public int ActiveOwnershipsCount { get; set; }

        public string? Login { get; init; }

        public decimal Balance { get; set; }

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

    private void NormalizeFilterState()
    {
        Status = NormalizeStatus(Status);
        SortBy = NormalizeSortBy(SortBy);
        SortDirection = NormalizeSortDirection(SortDirection);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    }

    private IEnumerable<MemberListItem> ApplySorting(IEnumerable<MemberListItem> members)
    {
        return (SortBy, SortDirection) switch
        {
            ("phone", "desc") => members.OrderByDescending(member => member.PhoneNumber).ThenBy(member => member.FullName),
            ("phone", _) => members.OrderBy(member => member.PhoneNumber).ThenBy(member => member.FullName),
            ("joinedat", "desc") => members.OrderByDescending(member => member.JoinedAt).ThenBy(member => member.FullName),
            ("joinedat", _) => members.OrderBy(member => member.JoinedAt).ThenBy(member => member.FullName),
            ("status", "desc") => members.OrderBy(member => member.IsActive ? 1 : 0).ThenBy(member => member.FullName),
            ("status", _) => members.OrderBy(member => member.IsActive ? 0 : 1).ThenBy(member => member.FullName),
            ("electricity", "desc") => members.OrderByDescending(member => member.IsElectricityDisconnected).ThenBy(member => member.FullName),
            ("electricity", _) => members.OrderBy(member => member.IsElectricityDisconnected).ThenBy(member => member.FullName),
            ("ownerships", "desc") => members.OrderByDescending(member => member.ActiveOwnershipsCount).ThenBy(member => member.FullName),
            ("ownerships", _) => members.OrderBy(member => member.ActiveOwnershipsCount).ThenBy(member => member.FullName),
            ("balance", "desc") => members.OrderByDescending(member => member.Balance).ThenBy(member => member.FullName),
            ("balance", _) => members.OrderBy(member => member.Balance).ThenBy(member => member.FullName),
            ("login", "desc") => members.OrderByDescending(member => member.Login).ThenBy(member => member.FullName),
            ("login", _) => members.OrderBy(member => member.Login).ThenBy(member => member.FullName),
            ("fullname", "desc") => members.OrderByDescending(member => member.FullName),
            _ => members.OrderBy(member => member.FullName)
        };
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return sortBy?.ToLowerInvariant() switch
        {
            "phone" => "phone",
            "joinedat" => "joinedat",
            "status" => "status",
            "electricity" => "electricity",
            "ownerships" => "ownerships",
            "balance" => "balance",
            "login" => "login",
            _ => "fullname"
        };
    }

    private static string NormalizeSortDirection(string? sortDirection)
    {
        return string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
    }
}
