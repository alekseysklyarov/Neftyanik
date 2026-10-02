using Neftyanik.Portal.Domain.Constants;
using static Neftyanik.Portal.Web.Localization.AppLocalizer;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public static class AssociationHistoryText
{
    public static string Action(string action) => action switch
    {
        PlatformAuditActions.AssociationCreated => Get("Товарищество создано", "Товариство створено", "Association created"),
        PlatformAuditActions.AssociationEdited => Get("Реквизиты изменены", "Реквізити змінено", "Details updated"),
        PlatformAuditActions.AssociationActivated => Get("Товарищество активировано", "Товариство активовано", "Association activated"),
        PlatformAuditActions.AssociationDeactivated => Get("Товарищество деактивировано", "Товариство деактивовано", "Association deactivated"),
        PlatformAuditActions.AdministratorAssigned => Get("Администратор назначен", "Адміністратора призначено", "Administrator assigned"),
        _ => Get("Другое действие", "Інша дія", "Other action")
    };

    public static string Field(string field) => field switch
    {
        "Name" => Get("Название", "Назва", "Name"),
        "Slug" => Get("Адрес товарищества (slug)", "Адреса товариства (slug)", "Association address (slug)"),
        "ContactEmail" => Get("Контактный email", "Контактний email", "Contact email"),
        "ContactPhone" => Get("Контактный телефон", "Контактний телефон", "Contact phone"),
        "PostalAddress" => Get("Почтовый адрес", "Поштова адреса", "Postal address"),
        "AdministratorUserName" => Get("Логин администратора", "Логін адміністратора", "Administrator username"),
        "AdministratorUserId" => Get("ID администратора", "ID адміністратора", "Administrator ID"),
        "Role" => Get("Роль", "Роль", "Role"),
        "IsActive" => Get("Статус", "Статус", "Status"),
        _ => Get("Поле", "Поле", "Field")
    };

    public static string Value(string field, string? value) => value is null or "" ? "—" : field switch
    {
        "IsActive" => value == "true" ? Get("Активно", "Активне", "Active") : Get("Неактивно", "Неактивне", "Inactive"),
        "Role" => Get("Администратор", "Адміністратор", "Administrator"),
        _ => value
    };
}
