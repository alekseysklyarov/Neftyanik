using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Payments;

[Authorize(Roles = RoleNames.Administrator + "," + RoleNames.Accountant + "," + RoleNames.Member)]
public class ReceiptLinksModel : PageModel
{
    private const int PageSize = 10;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReceiptLinksModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public async Task<IActionResult> OnGetAsync(
        string scope,
        int paymentPage = 1,
        int? plotId = null,
        int? memberId = null,
        CancellationToken cancellationToken = default)
    {
        paymentPage = paymentPage < 1 ? 1 : paymentPage;
        var currentDate = DateOnly.FromDateTime(DateTime.Today);

        IQueryable<Payment> paymentsQuery;
        int? receiptMemberId = null;

        if (string.Equals(scope, "member-dashboard", StringComparison.OrdinalIgnoreCase))
        {
            var member = await ResolveCurrentMemberAsync(cancellationToken);
            if (member is null)
            {
                return NotFound();
            }

            var plotIds = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .WhereCurrentForMember(member.Id, currentDate)
                .Select(ownership => ownership.PlotId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

            paymentsQuery = _dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.PlotId.HasValue && plotIds.Contains(payment.PlotId.Value));
        }
        else if (string.Equals(scope, "member-plot", StringComparison.OrdinalIgnoreCase))
        {
            if (!plotId.HasValue)
            {
                return BadRequest();
            }

            var member = await ResolveCurrentMemberAsync(cancellationToken);
            if (member is null)
            {
                return NotFound();
            }

            var ownsPlot = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .WhereCurrentForMember(member.Id, currentDate)
                .AnyAsync(ownership => ownership.PlotId == plotId.Value, cancellationToken);

            if (!ownsPlot)
            {
                return NotFound();
            }

            paymentsQuery = _dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.PlotId == plotId.Value);
        }
        else if (string.Equals(scope, "admin-member", StringComparison.OrdinalIgnoreCase))
        {
            if (!User.IsInRole(RoleNames.Administrator) && !User.IsInRole(RoleNames.Accountant))
            {
                return Forbid();
            }

            if (!memberId.HasValue)
            {
                return BadRequest();
            }

            var memberExists = await _dbContext.Members
                .AsNoTracking()
                .AnyAsync(member => member.Id == memberId.Value, cancellationToken);

            if (!memberExists)
            {
                return NotFound();
            }

            var plotIds = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .WhereCurrentForMember(memberId.Value, currentDate)
                .Select(ownership => ownership.PlotId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

            paymentsQuery = _dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.PlotId.HasValue && plotIds.Contains(payment.PlotId.Value));

            receiptMemberId = memberId.Value;
        }
        else
        {
            return BadRequest();
        }

        var paymentIds = await paymentsQuery
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.Id)
            .Skip((paymentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(payment => payment.Id)
            .ToListAsync(cancellationToken);

        return new JsonResult(new
        {
            paymentIds,
            memberId = receiptMemberId
        });
    }

    private async Task<Member?> ResolveCurrentMemberAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(RoleNames.Member))
        {
            return null;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null || user.MustChangePassword)
        {
            return null;
        }

        return await _dbContext.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(member => member.ApplicationUserId == user.Id, cancellationToken);
    }
}
