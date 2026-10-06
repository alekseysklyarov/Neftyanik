using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class DetailsModel(IPlatformAssociationReader associations, IPlatformOverviewReader overview) : PageModel
{
    public PlatformAssociationSetup Setup { get; private set; } = null!;
    public PlatformAssociationDetails Association { get; private set; } = null!;
    public PlatformAssociationHistoryPage History { get; private set; } = null!;

    [BindProperty(SupportsGet = true)]
    public int HistoryPage { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        var association = await associations.GetDetailsAsync(id, cancellationToken);
        if (association is null)
        {
            return NotFound();
        }

        Association = association;
        Setup = (await overview.GetSetupAsync(id, cancellationToken))!;
        if (Setup is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        History = await associations.GetHistoryAsync(id, HistoryPage, cancellationToken);
        HistoryPage = History.PageNumber;
        return Page();
    }
}
