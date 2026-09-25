using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Tests;

internal static class PrePlatformManagementDbContext
{
    public static ApplicationDbContext Create(DbContextOptions<ApplicationDbContext> options, IAssociationContext? association = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .ReplaceService<IModelCustomizer, HistoricalModelCustomizer>().Options, association);

    private sealed class HistoricalModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            builder.Ignore<AssociationAccountBinding>();
            builder.Ignore<PlatformAuditLog>();
            builder.Entity<Association>().Ignore(x => x.ContactEmail).Ignore(x => x.ContactPhone)
                .Ignore(x => x.PostalAddress).Ignore(x => x.Revision);
        }
    }
}
