using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Web.Associations;

public sealed class AssociationSlugReservations(IWebHostEnvironment environment) : IAssociationSlugReservations
{
    public bool IsReserved(string slug) => AssociationSlugRules.IsReserved(slug)
        || environment.WebRootFileProvider.GetDirectoryContents(string.Empty)
            .Any(entry => string.Equals(entry.Name, slug, StringComparison.OrdinalIgnoreCase));
}
