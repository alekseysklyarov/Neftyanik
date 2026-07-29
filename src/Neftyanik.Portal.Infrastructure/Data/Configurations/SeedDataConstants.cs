using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

internal static class SeedDataConstants
{
    public const string AdministratorRoleId = "role-administrator";
    public const string AccountantRoleId = "role-accountant";
    public const string MemberRoleId = "role-member";

    public const int InitialMembershipFeeRateId = 1;

    public static readonly DateOnly InitialMembershipFeeDueDate = new(2026, 12, 31);
    public static readonly DateTimeOffset SeedCreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly (int Id, string Name, bool IsActive)[] ExpenseCategories =
    [
        (1, "Электроэнергия", true),
        (2, "Охрана", true),
        (3, "Зарплата бухгалтеру и председателю", true),
        (4, "Покупка нового имущества для кооператива", true),
        (5, "Ремонт имущества для кооператива", true),
        (6, "Наемный труд для кооператива", true),
        (7, "Административные расходы", false),
        (8, "Налоги и банковские комиссии", false),
        (9, "Прочее", false)
    ];

    public static readonly (string Id, string Name)[] Roles =
    [
        (AdministratorRoleId, RoleNames.Administrator),
        (AccountantRoleId, RoleNames.Accountant),
        (MemberRoleId, RoleNames.Member)
    ];
}
