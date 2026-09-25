using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class DetailsModel(IPlatformAssociationReader associations) : PageModel
{
    public PlatformAssociationDetails Association { get; private set; } = null!;

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
        return Page();
    }
}
