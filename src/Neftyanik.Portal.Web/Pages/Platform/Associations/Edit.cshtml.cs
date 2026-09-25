using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class EditModel(IPlatformAssociationWriter writer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();
    public PlatformAssociationEditState Association { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var association = await writer.GetAsync(id, cancellationToken);
        if (association is null) return NotFound();
        Association = association;
        Input = new InputModel
        {
            Name = association.Name, ContactEmail = association.ContactEmail,
            ContactPhone = association.ContactPhone, PostalAddress = association.PostalAddress, Revision = association.Revision
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var association = await writer.GetAsync(id, cancellationToken);
        if (association is null) return NotFound();
        Association = association;
        if (!ModelState.IsValid) return Page();
        var result = await writer.UpdateAsync(id, new AssociationMetadataUpdate(Input.Name,
            Input.ContactEmail, Input.ContactPhone, Input.PostalAddress, Input.Revision!.Value), cancellationToken);
        if (result.Outcome is AssociationWriteOutcome.Saved or AssociationWriteOutcome.Unchanged)
        {
            TempData["SuccessMessage"] = AssociationWriteMessages.Success;
            return RedirectToPage("Details", new { id });
        }
        if (result.Outcome == AssociationWriteOutcome.NotFound) return NotFound();
        ModelState.AddModelError(result.InvalidField is null ? string.Empty : "Input." + result.InvalidField,
            AssociationWriteMessages.For(result.Outcome));
        if (result.Outcome == AssociationWriteOutcome.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
        if (result.Outcome == AssociationWriteOutcome.Failed) Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Вкажіть назву товариства.")]
        [StringLength(AssociationMetadataLimits.Name, ErrorMessage = "Назва має містити не більше 200 символів.")]
        public string Name { get; set; } = string.Empty;
        [EmailAddress(ErrorMessage = "Вкажіть коректну електронну адресу.")]
        [StringLength(AssociationMetadataLimits.ContactEmail, ErrorMessage = "Email має містити не більше 256 символів.")]
        public string? ContactEmail { get; set; }
        [StringLength(AssociationMetadataLimits.ContactPhone, ErrorMessage = "Телефон має містити не більше 50 символів.")]
        public string? ContactPhone { get; set; }
        [StringLength(AssociationMetadataLimits.PostalAddress, ErrorMessage = "Адреса має містити не більше 500 символів.")]
        public string? PostalAddress { get; set; }
        [Required(ErrorMessage = "Оновіть сторінку та повторіть спробу.")]
        public Guid? Revision { get; set; }
    }
}
