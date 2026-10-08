using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Settings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel(IAssociationReadinessService readinessService) : PageModel
{
    public AssociationReadinessSnapshot Readiness { get; private set; } = null!;
    public IReadOnlyList<ReadinessCard> Cards { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Readiness = await readinessService.GetAsync(cancellationToken);
        Cards = Readiness.Checks
            .Where(check => check.Area is not (ReadinessArea.ManualExpenseCategories
                or ReadinessArea.MembersAndPlots or ReadinessArea.MembershipFeeRates))
            .Select(check => check.Area == ReadinessArea.ElectricityExpenseCategory
                ? ReadinessCard.CreateExpenseCategories(check,
                    Readiness.Checks.Single(item => item.Area == ReadinessArea.ManualExpenseCategories), Readiness.Facts)
                : ReadinessCard.Create(check, Readiness.Facts)).ToArray();
    }
}
