global using Neftyanik.Portal.Tests;

using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Tests;

public static class TestAssociations
{
    public static AssociationContext Neftyanik => Resolved(1, "neftyanik");

    public static AssociationContext Resolved(int id, string slug)
    {
        var context = new AssociationContext();
        context.Resolve(new Association { Id = id, Name = slug, Slug = slug, IsActive = true });
        return context;
    }
}
