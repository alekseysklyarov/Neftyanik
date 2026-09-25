using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class IndexModel(IPlatformAssociationReader associations) : PageModel
{
    [BindProperty(SupportsGet = true)]
    [StringLength(200)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? IsActive { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PlatformAssociationPage Associations { get; private set; } = new([], 0, 1, 1);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        Associations = await associations.GetPageAsync(Search, IsActive, PageNumber, cancellationToken);
        PageNumber = Associations.PageNumber;
        return Page();
    }
}
