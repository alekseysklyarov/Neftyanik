using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

internal static class AssociationWriteMessages
{
    public static string For(AssociationWriteOutcome outcome) => outcome switch
    {
        AssociationWriteOutcome.Conflict => AppLocalizer.Get("Данные уже изменены. Обновите страницу.", "Дані вже змінено. Оновіть сторінку.", "The data has changed. Reload the page."),
        AssociationWriteOutcome.ProtectedAssociation => AppLocalizer.Get("Деактивация neftyanik запрещена.", "Деактивацію neftyanik заборонено.", "Deactivation of neftyanik is prohibited."),
        AssociationWriteOutcome.InvalidInput => AppLocalizer.Get("Проверьте введенные данные.", "Перевірте введені дані.", "Check the supplied values."),
        _ => AppLocalizer.Get("Не удалось сохранить изменения. Попробуйте позже.", "Не вдалося зберегти зміни. Спробуйте пізніше.", "Unable to save changes. Try again later.")
    };

    public static string Success => AppLocalizer.Get("Изменения сохранены.", "Зміни збережено.", "Changes saved.");
}
