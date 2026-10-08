using Microsoft.AspNetCore.Authorization;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Reports;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class ExpensesModel(ApplicationDbContext database) : Finance.Expenses.IndexModel(database)
{
}
