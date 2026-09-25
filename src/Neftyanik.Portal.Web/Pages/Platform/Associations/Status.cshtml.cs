using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class StatusModel(IPlatformAssociationWriter writer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();
    public PlatformAssociationEditState Association { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id, bool? activate, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !activate.HasValue) return BadRequest();
        var association = await writer.GetAsync(id, cancellationToken);
        if (association is null) return NotFound();
        Association = association;
        Input = new InputModel { IsActive = activate, Revision = association.Revision };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var association = await writer.GetAsync(id, cancellationToken);
        if (association is null) return NotFound();
        Association = association;
        if (!ModelState.IsValid) return Page();
        var result = await writer.SetActiveAsync(id, Input.IsActive!.Value, Input.Revision!.Value, cancellationToken);
        if (result.Outcome is AssociationWriteOutcome.Saved or AssociationWriteOutcome.Unchanged)
        {
            TempData["SuccessMessage"] = AssociationWriteMessages.Success;
            return RedirectToPage("Details", new { id });
        }
        if (result.Outcome == AssociationWriteOutcome.NotFound) return NotFound();
        ModelState.AddModelError(string.Empty, AssociationWriteMessages.For(result.Outcome));
        Response.StatusCode = result.Outcome switch
        {
            AssociationWriteOutcome.Conflict => StatusCodes.Status409Conflict,
            AssociationWriteOutcome.ProtectedAssociation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Вкажіть дію.")]
        public bool? IsActive { get; set; }
        [Required(ErrorMessage = "Оновіть сторінку та повторіть спробу.")]
        public Guid? Revision { get; set; }
    }
}
