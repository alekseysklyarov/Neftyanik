using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public static class ElectricityExpenseCategoryQueries
{
    public const string SettingKey = "Finance.ElectricityExpenseCategoryId";

    public static async Task<int?> GetElectricityExpenseCategoryIdAsync(this ApplicationDbContext database, CancellationToken cancellationToken = default)
    {
        if (!database.IsAssociationResolved)
        {
            return null;
        }

        var configuredValue = await database.SystemSettings.AsNoTracking()
            .Where(x => x.AssociationId == database.CurrentAssociationId && x.Key == SettingKey)
            .Select(x => x.Value).SingleOrDefaultAsync(cancellationToken);
        var categoryId = ExpenseCategoryIds.ElectricityPayment;
        if (configuredValue is not null && (!int.TryParse(configuredValue, NumberStyles.None, CultureInfo.InvariantCulture, out categoryId) || categoryId <= 0))
        {
            return null;
        }

        // Preserve the legacy category only if it actually belongs to this association.
        return await database.ExpenseCategories.AsNoTracking()
            .Where(x => x.AssociationId == database.CurrentAssociationId && x.Id == categoryId)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
    }
}
