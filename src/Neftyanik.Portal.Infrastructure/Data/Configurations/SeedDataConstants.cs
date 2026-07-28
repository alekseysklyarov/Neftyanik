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

    public static readonly (int Id, string Name)[] ExpenseCategories =
    [
        (1, "Оплата электроэнергии"),
        (2, "Ремонт электросети"),
        (3, "Ремонт дорог"),
        (4, "Охрана"),
        (5, "Вывоз мусора"),
        (6, "Обслуживание территории"),
        (7, "Административные расходы"),
        (8, "Налоги и банковские комиссии"),
        (9, "Прочее")
    ];

    public static readonly (string Id, string Name)[] Roles =
    [
        (AdministratorRoleId, RoleNames.Administrator),
        (AccountantRoleId, RoleNames.Accountant),
        (MemberRoleId, RoleNames.Member)
    ];
}
