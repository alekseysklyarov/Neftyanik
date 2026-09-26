using Neftyanik.Portal.Application.Finance;
using static Neftyanik.Portal.Web.Localization.AppLocalizer;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Settings;

public sealed record ReadinessLink(string Page, string Text);

public sealed record ReadinessCard(
    ReadinessArea Area, string Title, string Status, string BadgeClass, string Reason,
    string Details, string Limitation, IReadOnlyList<ReadinessLink> Links)
{
    internal static ReadinessCard Create(ReadinessCheck check, AssociationReadinessFacts facts)
    {
        var (title, operation, links) = DescribeArea(check.Area);
        var limited = check.Status is ReadinessStatus.RequiredForOperation or ReadinessStatus.NotUsed or ReadinessStatus.NotImplemented;
        return new(check.Area, title, StatusText(check.Status), check.Status switch
        {
            ReadinessStatus.Ready => "text-bg-success",
            ReadinessStatus.RequiredForOperation or ReadinessStatus.Warning => "text-bg-warning",
            _ => "text-bg-secondary"
        }, ReasonText(check.Reason), DetailsText(check.Area, facts), limited ? operation
            : Get("Эта проверка не ограничивает операции.", "Ця перевірка не обмежує операції.", "This check does not limit operations."), links);
    }

    private static string StatusText(ReadinessStatus status) => status switch
    {
        ReadinessStatus.Ready => Get("Готово", "Готово", "Ready"),
        ReadinessStatus.RequiredForOperation => Get("Нужно для конкретной операции", "Потрібно для конкретної операції", "Required for a specific operation"),
        ReadinessStatus.Warning => Get("Предупреждение", "Попередження", "Warning"),
        ReadinessStatus.NotUsed => Get("Не используется / нет данных", "Не використовується / немає даних", "Not used / no data"),
        _ => Get("Функция не реализована", "Функцію не реалізовано", "Not implemented")
    };

    private static (string Title, string Operation, ReadinessLink[] Links) DescribeArea(ReadinessArea area) => area switch
    {
        ReadinessArea.MemberElectricity => (
            Get("Электроэнергия участников", "Електроенергія учасників", "Member electricity"),
            Get("Для расчёта начислений за электроэнергию нужен подходящий тариф на дату показания.", "Для розрахунку нарахувань за електроенергію потрібен відповідний тариф на дату показання.", "Electricity charges require an applicable tariff on the reading date."),
            [new("/Administration/Electricity/MemberTariffs/Index", Get("Тарифы участников", "Тарифи учасників", "Member tariffs"))]),
        ReadinessArea.SupplierElectricity => (
            Get("Электроэнергия поставщика", "Електроенергія постачальника", "Supplier electricity"),
            Get("Для расчёта расхода общего счётчика нужны начальное показание и тариф. Категория расхода проверяется отдельно.", "Для розрахунку витрат загального лічильника потрібні початкове показання й тариф. Категорія витрат перевіряється окремо.", "Shared meter expenses require an initial reading and tariff. The expense category is checked separately."),
            [new("/Administration/Electricity/Association/Tariffs/Index", Get("Тарифы поставщика", "Тарифи постачальника", "Supplier tariffs")),
             new("/Administration/Electricity/Association/Initial", Get("Начальное показание", "Початкове показання", "Initial reading")),
             new("/Administration/Electricity/Association/Index", Get("История показаний", "Історія показань", "Reading history"))]),
        ReadinessArea.IndividualMeters => (
            Get("Индивидуальные счётчики", "Індивідуальні лічильники", "Individual meters"),
            Get("Расчёт потребления соответствующего счётчика требует начального показания и подходящего тарифа.", "Розрахунок споживання відповідного лічильника потребує початкового показання й відповідного тарифу.", "Consumption calculation for an affected meter requires an initial reading and applicable tariff."),
            [new("/Administration/Electricity/Meters/Index", Get("Счётчики и начальные показания", "Лічильники та початкові показання", "Meters and initial readings")),
             new("/Administration/Electricity/MemberTariffs/Index", Get("Тарифы участников", "Тарифи учасників", "Member tariffs"))]),
        ReadinessArea.ChargeTypes => (
            Get("Типы начислений", "Типи нарахувань", "Charge types"),
            Get("Для ручных начислений нужен активный тип начисления.", "Для ручних нарахувань потрібен активний тип нарахування.", "Manual charges require an active charge type."),
            [new("/Administration/Finance/ChargeTypes/Index", Get("Типы начислений", "Типи нарахувань", "Charge types"))]),
        ReadinessArea.ElectricityExpenseCategory => (
            Get("Системная категория электроэнергии", "Системна категорія електроенергії", "System electricity category"),
            Get("Регистрация расходов поставщика требует корректной системной категории. Создание обычной категории не исправляет SystemSetting; обратитесь к администратору.", "Реєстрація витрат постачальника потребує коректної системної категорії. Створення звичайної категорії не виправляє SystemSetting; зверніться до адміністратора.", "Supplier expense registration requires a valid system category. Creating an ordinary category does not repair SystemSetting; contact the administrator."),
            [new("/Administration/Finance/ExpenseCategories/Index", Get("Категории расходов", "Категорії витрат", "Expense categories"))]),
        ReadinessArea.ManualExpenseCategories => (
            Get("Категории ручных расходов", "Категорії ручних витрат", "Manual expense categories"),
            Get("Для ручного расхода нужна обычная активная категория.", "Для ручної витрати потрібна звичайна активна категорія.", "Manual expenses require an ordinary active category."),
            [new("/Administration/Finance/ExpenseCategories/Index", Get("Категории расходов", "Категорії витрат", "Expense categories"))]),
        ReadinessArea.Cash => (
            Get("Касса", "Каса", "Cash"),
            Get("Инициализация кассы необязательна.", "Ініціалізація каси необов’язкова.", "Cash initialization is optional."),
            [new("/Administration/Finance/Settings/CashInitialization", Get("Инициализация кассы", "Ініціалізація каси", "Cash initialization"))]),
        ReadinessArea.MembersAndPlots => (
            Get("Участники и участки", "Учасники та ділянки", "Members and plots"),
            Get("Для приёма платежа участника сначала создайте действующую связь владения участком.", "Для приймання платежу учасника спочатку створіть чинний зв’язок володіння ділянкою.", "To accept a member payment, first create a current plot ownership."),
            [new("/Administration/Members/Index", Get("Участники", "Учасники", "Members")),
             new("/Administration/Plots/Index", Get("Участки и владельцы", "Ділянки та власники", "Plots and owners"))]),
        _ => (
            "MembershipFeeRate",
            Get("Автоматическое начисление через MembershipFeeRate недоступно. Ежегодные начисления создаются через существующие типы начислений.", "Автоматичне нарахування через MembershipFeeRate недоступне. Щорічні нарахування створюються через наявні типи нарахувань.", "Automatic billing through MembershipFeeRate is unavailable. Yearly charges use the existing charge types."),
            [new("/Administration/Finance/ChargeTypes/Index", Get("Существующие типы начислений", "Наявні типи нарахувань", "Existing charge types"))])
    };

    private static string ReasonText(ReadinessReason reason) => reason switch
    {
        ReadinessReason.Configured => Get("Необходимые данные этой проверки есть.", "Необхідні дані цієї перевірки є.", "The data required by this check is available."),
        ReadinessReason.NoApplicableTariff => Get("На сегодня нет действующего тарифа. Будущие тарифы не учитываются.", "На сьогодні немає чинного тарифу. Майбутні тарифи не враховуються.", "No tariff applies today. Future tariffs are not counted."),
        ReadinessReason.MissingNightTariff => Get("Есть активный двухзонный счётчик, но нет ночной ставки в действующем тарифе.", "Є активний двозонний лічильник, але немає нічної ставки в чинному тарифі.", "An active day/night meter needs a night rate in the current tariff."),
        ReadinessReason.NoMeters => Get("Активных индивидуальных счётчиков нет. Это не ошибка.", "Активних індивідуальних лічильників немає. Це не помилка.", "There are no active individual meters. This is not an error."),
        ReadinessReason.NoSupplierHistory => Get("Нет истории общего счётчика; его наличие определяется по показаниям. Можно ввести начальное показание.", "Немає історії загального лічильника; його наявність визначається за показаннями. Можна ввести початкове показання.", "No shared meter history; its presence is determined by readings. An initial reading can be entered."),
        ReadinessReason.MissingInitialReading => Get("В истории нет начального показания. Проверьте существующие данные; повторная инициализация при наличии истории недоступна.", "В історії немає початкового показання. Перевірте наявні дані; повторна ініціалізація за наявності історії недоступна.", "History has no initial reading. Review existing data; initialization is unavailable when history already exists."),
        ReadinessReason.MissingTariffAndInitialReading => Get("Нет действующего тарифа и начального показания. Существующую историю нужно проверить отдельно.", "Немає чинного тарифу й початкового показання. Наявну історію потрібно перевірити окремо.", "No current tariff or initial reading. Existing history needs a separate review."),
        ReadinessReason.IncompleteMeters => Get("Не всем активным счётчикам хватает начальных показаний или тарифа на сегодня.", "Не всім активним лічильникам вистачає початкових показань або тарифу на сьогодні.", "Some active meters lack initial readings or a tariff for today."),
        ReadinessReason.NoActiveChargeTypes => Get("Нет активных типов начислений.", "Немає активних типів нарахувань.", "No active charge types."),
        ReadinessReason.MissingCategorySetting => Get("Отсутствует Finance.ElectricityExpenseCategoryId и подходящая системная категория.", "Відсутні Finance.ElectricityExpenseCategoryId і відповідна системна категорія.", "Finance.ElectricityExpenseCategoryId and a suitable system category are missing."),
        ReadinessReason.LegacyCategoryFallback => Get("Finance.ElectricityExpenseCategoryId отсутствует; используется совместимая системная категория этого товарищества.", "Finance.ElectricityExpenseCategoryId відсутній; використовується сумісна системна категорія цього товариства.", "Finance.ElectricityExpenseCategoryId is absent; the association's legacy system category is used."),
        ReadinessReason.InvalidCategorySetting => Get("Finance.ElectricityExpenseCategoryId содержит некорректный идентификатор.", "Finance.ElectricityExpenseCategoryId містить некоректний ідентифікатор.", "Finance.ElectricityExpenseCategoryId contains an invalid identifier."),
        ReadinessReason.CategoryUnavailable => Get("Системная настройка не указывает на активную категорию этого товарищества.", "Системне налаштування не вказує на активну категорію цього товариства.", "The system setting does not reference an active category of this association."),
        ReadinessReason.NoManualCategories => Get("Нет обычной активной категории для ручных расходов.", "Немає звичайної активної категорії для ручних витрат.", "No ordinary active category for manual expenses."),
        ReadinessReason.UnknownCategoryMapping => Get("Есть активные категории, но без корректной системной связи нельзя надёжно выделить обычные категории.", "Є активні категорії, але без коректного системного зв’язку неможливо надійно визначити звичайні категорії.", "Active categories exist, but ordinary categories cannot be reliably identified without a valid system mapping."),
        ReadinessReason.CashStartsAtZero => Get("Учёт начинается с нулевого остатка", "Облік починається з нульового залишку", "Accounting starts with a zero balance"),
        ReadinessReason.InvalidCashInitialization => Get("Запись инициализации кассы требует проверки администратором; обзор её не изменяет.", "Запис ініціалізації каси потребує перевірки адміністратором; огляд його не змінює.", "Cash initialization needs administrator review; this overview does not change it."),
        ReadinessReason.NoMembersOrPlots => Get("Участники или участки ещё не созданы.", "Учасників або ділянки ще не створено.", "Members or plots have not been created yet."),
        ReadinessReason.NoCurrentOwnership => Get("Нет действующих на сегодня связей владения участками.", "Немає чинних на сьогодні зв’язків володіння ділянками.", "No plot ownerships are current today."),
        _ => Get("Автоматические ставки членских взносов пока не подключены к начислениям", "Автоматичні ставки членських внесків поки не підключені до нарахувань", "Automatic membership fee rates are not yet connected to billing")
    };

    private static string DetailsText(ReadinessArea area, AssociationReadinessFacts facts) => area switch
    {
        ReadinessArea.MemberElectricity => facts.MemberTariffEffectiveFrom is { } date
            ? Get($"Тариф действует с {date:dd.MM.yyyy}.", $"Тариф діє з {date:dd.MM.yyyy}.", $"Tariff effective from {date:yyyy-MM-dd}.") : "",
        ReadinessArea.SupplierElectricity => facts.SupplierTariffEffectiveFrom is { } date
            ? Get($"Тариф действует с {date:dd.MM.yyyy}.", $"Тариф діє з {date:dd.MM.yyyy}.", $"Tariff effective from {date:yyyy-MM-dd}.")
            : Get("Действующего тарифа поставщика нет.", "Чинного тарифу постачальника немає.", "No current supplier tariff."),
        ReadinessArea.IndividualMeters => Get(
            $"Активных: {facts.ActiveMeters}; без начальных показаний: {facts.MetersWithoutInitialReading}; без подходящего тарифа: {facts.MetersWithoutApplicableTariff}.",
            $"Активних: {facts.ActiveMeters}; без початкових показань: {facts.MetersWithoutInitialReading}; без відповідного тарифу: {facts.MetersWithoutApplicableTariff}.",
            $"Active: {facts.ActiveMeters}; without initial readings: {facts.MetersWithoutInitialReading}; without applicable tariff: {facts.MetersWithoutApplicableTariff}."),
        ReadinessArea.ChargeTypes => Get($"Активных: {facts.ActiveChargeTypes}; ежегодных: {facts.YearlyChargeTypes}. Ежегодный тип не обязательно является членским взносом.",
            $"Активних: {facts.ActiveChargeTypes}; щорічних: {facts.YearlyChargeTypes}. Щорічний тип не обов’язково є членським внеском.",
            $"Active: {facts.ActiveChargeTypes}; yearly: {facts.YearlyChargeTypes}. A yearly type is not necessarily a membership fee."),
        ReadinessArea.ManualExpenseCategories => Get($"Активных, кроме определённой системной категории: {facts.ActiveManualExpenseCategories}.",
            $"Активних, крім визначеної системної категорії: {facts.ActiveManualExpenseCategories}.", $"Active, excluding the identified system category: {facts.ActiveManualExpenseCategories}."),
        ReadinessArea.Cash => facts.CashInitializedOn is { } date
            ? Get($"Инициализация настроена на {date:dd.MM.yyyy}.", $"Ініціалізацію налаштовано на {date:dd.MM.yyyy}.", $"Initialization configured for {date:yyyy-MM-dd}.") : "",
        ReadinessArea.MembersAndPlots => Get($"Участников: {facts.Members}; участков: {facts.Plots}; действующих связей владения: {facts.CurrentOwnerships}.",
            $"Учасників: {facts.Members}; ділянок: {facts.Plots}; чинних зв’язків володіння: {facts.CurrentOwnerships}.",
            $"Members: {facts.Members}; plots: {facts.Plots}; current ownerships: {facts.CurrentOwnerships}."),
        _ => ""
    };
}
