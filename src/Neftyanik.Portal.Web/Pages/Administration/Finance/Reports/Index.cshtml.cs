using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Reports;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
}
