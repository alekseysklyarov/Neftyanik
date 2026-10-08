using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Infrastructure.Services;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class AdministratorModel(IPlatformAssociationReader reader, PlatformAssociationAdministratorManager manager) : PageModel
{
    public PlatformAssociationAdministrator Administrator { get; private set; } = null!;
    [BindProperty] public string? TemporaryPassword { get; set; }
    public async Task<IActionResult> OnGetAsync(int id, string userName, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var association = await reader.GetDetailsAsync(id, ct);
        var administrator = association?.Administrators.SingleOrDefault(a => a.UserName == userName);
        if (administrator is null) return NotFound();
        Administrator = administrator;
        return Page();
    }
    public async Task<IActionResult> OnPostStatusAsync(int id, string userName, bool active, CancellationToken ct)
        => await ChangeAsync(id, userName, active, null, ct);
    public async Task<IActionResult> OnPostPasswordAsync(int id, string userName, CancellationToken ct)
    {
        var password = TemporaryPassword;
        TemporaryPassword = null;
        ModelState.Remove(nameof(TemporaryPassword));
        return await ChangeAsync(id, userName, null, password, ct);
    }
    private async Task<IActionResult> ChangeAsync(int id, string userName, bool? active, string? password, CancellationToken ct)
    {
        var page = await OnGetAsync(id, userName, ct);
        if (page is NotFoundResult) return page;
        var error = await manager.ChangeAsync(id, userName, active, password, ct);
        if (error is not null) { ModelState.AddModelError(string.Empty, error); return Page(); }
        TempData["SuccessMessage"] = active.HasValue ? "Доступ администратора обновлён." : "Временный пароль установлен. При входе потребуется смена пароля.";
        return RedirectToPage("Details", new { id });
    }
}
