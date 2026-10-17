using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Sales;

namespace Neftyanik.Portal.Web.Pages.Platform.Requests;

public class IndexModel(ApplicationDbContext database, SalesNotificationSettings notifications) : PageModel
{
    public bool NotificationsConfigured => notifications.IsReady;
    public string NotificationRecipient => notifications.Options.Recipient;
    [BindProperty(SupportsGet = true)] public bool ShowContacted { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public IReadOnlyList<DemoRequest> Requests { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid) return BadRequest();
        var query = database.DemoRequests.AsNoTracking().Where(x => ShowContacted ? x.ContactedAtUtc != null : x.ContactedAtUtc == null);
        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / 20.0));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Requests = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((PageNumber - 1) * 20).Take(20).ToListAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(long id, bool contacted, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var request = await database.DemoRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return NotFound();
        request.ContactedAtUtc = contacted ? request.ContactedAtUtc ?? DateTime.UtcNow : null;
        await database.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { ShowContacted, PageNumber });
    }
}
