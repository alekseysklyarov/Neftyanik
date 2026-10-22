using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Web.Documentation;

namespace Neftyanik.Portal.Web.Pages.Product;

[AllowAnonymous]
public class DocumentationModel : PageModel
{
    public IReadOnlyList<GuideArticle> Articles { get; private set; } = [];
    public void OnGet() => Articles = DocumentationContent.Articles();
}
