using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Xunit;
using FinancePage = Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel;
using ExpensesPage = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.IndexModel;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;

namespace Neftyanik.Portal.Web.Tests;

public class FinancialIsolationTests
{
    private const string SharedUser = "finance-shared";
    private static readonly DateOnly Date = new(DateTime.Today.Year, 1, 10);

    [Theory]
    [InlineData("neftyanik", "finance-b")]
    [InlineData("finance-b", "neftyanik")]
    public async Task AddingAndOperatingInOtherAssociation_DoesNotChangeTotalsOrHistory(string unchanged, string changed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = await fixture.SnapshotAsync(unchanged);
        await fixture.ExecuteAsync(changed, async (services, database) =>
        {
            var data = fixture.Data[changed];
            var notification = await services.GetRequiredService<IPaymentNotificationService>().CreateAsync(data.MemberId,
                new CreatePaymentNotificationRequest(123m, PaymentMethod.Cash, "new-local-notification"));
            Assert.True(notification.Succeeded);
            var payment = await services.GetRequiredService<IPaymentService>().CreateMemberPaymentAsync(new CreateMemberPaymentRequest(
                data.MemberId, data.PlotId, Date, 234m, PaymentMethod.Cash, null, "new-local-payment", SharedUser));
            Assert.True(payment.Succeeded);
            database.Charges.Add(new Charge { PlotId = data.PlotId, ChargeTypeId = data.ChargeTypeId, Amount = 345m, ChargeDate = Date });
            database.Expenses.Add(new Expense { ExpenseCategoryId = data.ManualCategoryId, ExpenseDate = Date, Amount = 56m, Description = "new-local-expense", CreatedByUserId = SharedUser });
            var cash = await database.SystemSettings.SingleAsync(x => x.Key == "Finance.CashInitialization");
            cash.Value = JsonSerializer.Serialize(new { Amount = 777m, AcceptedAt = Date.AddDays(-5), AcceptedFrom = changed, AdvancePaymentsAmount = 17m });
            await database.SaveChangesAsync();
            var meters = services.GetRequiredService<IMemberElectricityService>();
            Assert.True((await meters.CreateTariffAsync(new(Date, 9m * data.Factor, null, SharedUser))).Succeeded);
            Assert.True((await meters.CreateReadingAsync(new(data.MeterId, Date, 100m * data.Factor + 11m, null, SharedUser))).Succeeded);
            var common = services.GetRequiredService<IAssociationElectricityService>();
            Assert.True((await common.CreateTariffAsync(new(Date, 11m, 4m, SharedUser))).Succeeded);
            var reading = await common.CreateReadingAsync(new(Date, 1000m * data.Factor + 21m, 500m * data.Factor + 11m, SharedUser));
            Assert.True(reading.Succeeded);
            Assert.True((await common.CreateExpenseAsync(new(reading.ReadingId!.Value, SharedUser))).Succeeded);
        });
        Assert.Equal(baseline, await fixture.SnapshotAsync(unchanged));
        await fixture.AssertExpectedAsync(unchanged);
    }

    [Theory]
    [InlineData("neftyanik", 750, 0, 580)]
    [InlineData("finance-b", 0, 750, 4740)]
    public async Task SharedUser_HasIndependentDebtOverpaymentCashAndNavigationTotals(string slug, int debt, int overpayment, int cash)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AssertExpectedAsync(slug);
        await fixture.ExecuteAsync(slug, async (_, database) =>
        {
            var page = new FinancePage(database);
            await page.OnGetAsync(CancellationToken.None);
            Assert.Equal(debt, page.Summary.TotalCurrentDebt);
            Assert.Equal(overpayment, page.Summary.TotalOverpayments);
            Assert.Equal(cash, page.Summary.CurrentCashAmount);
            var member = await database.Members.SingleAsync(x => x.ApplicationUserId == SharedUser);
            Assert.Equal(fixture.Data[slug].MemberId, member.Id);
        });
        using var client = fixture.Factory.CreateAuthenticatedClient(new TestAuthenticatedUser(SharedUser, RoleNames.Member), associationSlug: slug);
        var own = fixture.Data[slug];
        var foreign = fixture.Data[slug == "neftyanik" ? "finance-b" : "neftyanik"];
        foreach (var path in new[] { "/Member", $"/Payments/{own.PaymentId}/Receipt", "/Member/Electricity" })
        {
            using var response = await client.GetAsync($"/{slug}{path}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.ReadDecodedHtmlAsync();
            Assert.DoesNotContain($"marker-{foreign.Slug}", html);
        }
        using var receipt = await client.GetAsync($"/{slug}/Payments/{foreign.PaymentId}/Receipt");
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Administrator)]
    [InlineData(RoleNames.Accountant)]
    public async Task ForeignFinancialIds_CannotBeReadOrMutatedThroughRazorPages(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeA = await fixture.SnapshotAsync("neftyanik");
        var beforeB = await fixture.SnapshotAsync("finance-b");
        using var client = fixture.Factory.CreateAuthenticatedClient(new TestAuthenticatedUser("finance-operator", role));
        var foreign = fixture.Data["finance-b"];
        var own = fixture.Data["neftyanik"];
        foreach (var path in new[] { "/Administration/Finance", "/Administration/Finance/Expenses", "/Administration/Finance/PaymentNotifications",
            "/Administration/Electricity/Meters", "/Administration/Electricity/Association", "/Administration/AuditLog" })
        {
            using var response = await client.GetAsync("/neftyanik" + path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("marker-finance-b", await response.ReadDecodedHtmlAsync());
        }
        foreach (var path in new[]
        {
            $"/Payments/{foreign.PaymentId}/Receipt?memberId={foreign.MemberId}",
            $"/Administration/Members/Finance/{foreign.MemberId}/Finance",
            $"/Administration/Members/Finance/{own.MemberId}/Charges/{foreign.ChargeId}/Cancel",
            $"/Administration/Members/Finance/{own.MemberId}/Payments/{foreign.PaymentId}/Cancel",
            $"/Administration/Finance/Expenses/Details/{foreign.ExpenseId}",
            $"/Administration/Finance/Expenses/Edit/{foreign.ExpenseId}",
            $"/Administration/Electricity/Meters/Details/{foreign.MeterId}",
            $"/Administration/AuditLog/{foreign.AuditId}"
        })
        {
            using var response = await client.GetAsync("/neftyanik" + path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        foreach (var path in new[]
        {
            $"/Administration/Members/Finance/{own.MemberId}/Charges/{foreign.ChargeId}/Cancel",
            $"/Administration/Members/Finance/{own.MemberId}/Payments/{foreign.PaymentId}/Cancel",
            $"/Administration/Finance/Expenses/Cancel/{foreign.ExpenseId}"
        })
        {
            using var response = await client.PostAsync("/neftyanik" + path, Form(token,
                ("Input.CancellationReason", "forged cancellation"), ("AssociationId", foreign.AssociationId.ToString())));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(beforeA, await fixture.SnapshotAsync("neftyanik"));
        Assert.Equal(beforeB, await fixture.SnapshotAsync("finance-b"));
    }

    [Theory]
    [InlineData("neftyanik", "finance-b")]
    [InlineData("finance-b", "neftyanik")]
    public async Task ForeignServiceIds_RejectApprovalAllocationCancellationAndElectricityMutations(string current, string other)
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeCurrent = await fixture.SnapshotAsync(current);
        var beforeOther = await fixture.SnapshotAsync(other);
        await fixture.ExecuteAsync(current, async (services, _) =>
        {
            var own = fixture.Data[current];
            var foreign = fixture.Data[other];
            var notifications = services.GetRequiredService<IPaymentNotificationService>();
            Assert.Equal(PaymentNotificationOperationResultCode.NotFound, (await notifications.ConfirmAsync(new(foreign.NotificationId, Date, own.PlotId, SharedUser))).Code);
            Assert.False((await notifications.ConfirmAsync(new(own.NotificationId, Date, foreign.PlotId, SharedUser))).Succeeded);
            Assert.Equal(PaymentNotificationOperationResultCode.NotFound, (await notifications.RejectAsync(new(foreign.NotificationId, SharedUser, "foreign"))).Code);
            Assert.False((await notifications.CreateAsync(foreign.MemberId, new(1m, PaymentMethod.Cash, null))).Succeeded);
            Assert.Empty(await notifications.GetRecentForMemberAsync(foreign.MemberId));
            var payments = services.GetRequiredService<IPaymentService>();
            Assert.False((await payments.CreateMemberPaymentAsync(new(own.MemberId, foreign.PlotId, Date, 1m, PaymentMethod.Cash, null, null, SharedUser))).Succeeded);
            Assert.False((await payments.CreateMemberPaymentAsync(new(foreign.MemberId, foreign.PlotId, Date, 1m, PaymentMethod.Cash, null, null, SharedUser))).Succeeded);
            Assert.Equal(CancelPaymentResultCode.NotFound, (await payments.CancelPaymentAsync(new(foreign.PaymentId, "foreign"))).Code);
            Assert.Equal(CancelChargeResultCode.NotFound, (await services.GetRequiredService<IChargeService>().CancelChargeAsync(new(foreign.ChargeId, "foreign"))).Code);
            var meters = services.GetRequiredService<IMemberElectricityService>();
            Assert.Null(await meters.GetReadingEntryContextAsync(foreign.MeterId, Date.AddDays(2), 999m, null));
            Assert.False((await meters.CreateReadingAsync(new(foreign.MeterId, Date.AddDays(2), 999m, null, SharedUser))).Succeeded);
            Assert.False((await meters.UpdateMeterAsync(new(foreign.MeterId, own.MemberId, "forged", "forged", false, own.PlotId, new[] { own.PlotId }, SharedUser))).Succeeded);
            Assert.False((await meters.CreateMeterAsync(new(foreign.MemberId, "forged", "forged", true, foreign.PlotId, new[] { foreign.PlotId }, SharedUser))).Succeeded);
            Assert.False((await services.GetRequiredService<IAssociationElectricityService>().CreateExpenseAsync(new(foreign.CommonReadingId, SharedUser))).Succeeded);
        });
        Assert.Equal(beforeCurrent, await fixture.SnapshotAsync(current));
        Assert.Equal(beforeOther, await fixture.SnapshotAsync(other));
    }

    [Theory]
    [InlineData("neftyanik", "finance-b")]
    [InlineData("finance-b", "neftyanik")]
    public async Task LocalApprovalAllocationAndCancellation_LeaveOtherAssociationUnchanged(string current, string other)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync(other);
        await fixture.ExecuteAsync(current, async (services, database) =>
        {
            var own = fixture.Data[current];
            var result = await services.GetRequiredService<IPaymentNotificationService>().ConfirmAsync(new(own.NotificationId, Date, own.PlotId, SharedUser));
            Assert.True(result.Succeeded);
            Assert.Equal(PaymentNotificationStatus.Confirmed, (await database.PaymentNotifications.SingleAsync(x => x.Id == own.NotificationId)).Status);
            var allocations = await database.PaymentAllocations.Where(x => x.PaymentId == result.PaymentId).Include(x => x.Charge).ToListAsync();
            Assert.NotEmpty(allocations);
            Assert.All(allocations, x => Assert.Equal(own.AssociationId, x.Charge!.AssociationId));
            Assert.True((await services.GetRequiredService<IPaymentService>().CancelPaymentAsync(new(result.PaymentId!.Value, "local cancellation"))).Succeeded);
            Assert.True((await services.GetRequiredService<IChargeService>().CancelChargeAsync(new(own.ChargeId, "local cancellation"))).Succeeded);
        });
        Assert.Equal(before, await fixture.SnapshotAsync(other));
    }

    [Fact]
    public async Task ElectricityExpense_UsesLocalCategoryAndLocalSupplierTariff()
    {
        await using var fixture = await Fixture.CreateAsync();
        foreach (var slug in new[] { "neftyanik", "finance-b" })
        {
            await fixture.ExecuteAsync(slug, async (_, database) =>
            {
                var data = fixture.Data[slug];
                var expense = await database.Expenses.Include(x => x.ExpenseCategory).Include(x => x.AssociationElectricityReading)
                    .SingleAsync(x => x.AssociationElectricityReadingId == data.CommonReadingId);
                Assert.Equal(data.ElectricityCategoryId, expense.ExpenseCategoryId);
                Assert.Equal(data.AssociationId, expense.ExpenseCategory!.AssociationId);
                Assert.Equal(170m * data.Factor, expense.Amount);
                Assert.Equal(30m, expense.AssociationElectricityReading!.TotalConsumption);
                var page = new ExpensesPage(database) { Kind = "electricity" };
                await page.OnGetAsync(CancellationToken.None);
                Assert.Single(page.Expenses);
                Assert.Equal(170m * data.Factor, page.Summary.ElectricityExpenses);
                Assert.Equal(40m * data.Factor, page.Summary.ManualExpenses);
            });
        }
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("invalid")]
    public async Task ElectricityCategory_MissingOrForeignMappingFailsWithoutWritingExpenses(string mapping)
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeA = await fixture.SnapshotAsync("neftyanik");
        await fixture.ExecuteAsync("finance-b", async (services, database) =>
        {
            var setting = await database.SystemSettings.SingleAsync(x => x.Key == ElectricityExpenseCategoryQueries.SettingKey);
            if (mapping == "missing") database.SystemSettings.Remove(setting);
            else setting.Value = mapping == "foreign" ? fixture.Data["neftyanik"].ElectricityCategoryId.ToString() : "not-an-id";
            await database.SaveChangesAsync();
            Assert.Null(await database.GetElectricityExpenseCategoryIdAsync());
            var common = services.GetRequiredService<IAssociationElectricityService>();
            var reading = await common.CreateReadingAsync(new(Date, 3021m, 1511m, SharedUser));
            Assert.True(reading.Succeeded);
            var expensesBefore = await database.Expenses.CountAsync();
            var auditsBefore = await database.FinancialAuditLogs.CountAsync();
            Assert.False((await common.CreateExpenseAsync(new(reading.ReadingId!.Value, SharedUser))).Succeeded);
            Assert.Equal(expensesBefore, await database.Expenses.CountAsync());
            Assert.Equal(auditsBefore, await database.FinancialAuditLogs.CountAsync());
        });
        Assert.Equal(beforeA, await fixture.SnapshotAsync("neftyanik"));
    }

    [Fact]
    public async Task ElectricityCategory_ProtectionAndSelectorsUseCurrentAssociationMapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Factory.CreateAuthenticatedClient(new TestAuthenticatedUser("b-accountant", RoleNames.Accountant), associationSlug: "finance-b");
        var data = fixture.Data["finance-b"];
        var token = await AuthenticationCookieTests.TokenAsync(client, "/finance-b/Privacy");
        using var archive = await client.PostAsync($"/finance-b/Administration/Finance/ExpenseCategories/Archive/{data.ElectricityCategoryId}", Form(token));
        Assert.Equal(HttpStatusCode.Found, archive.StatusCode);
        await fixture.ExecuteAsync("finance-b", async (services, database) =>
        {
            Assert.True((await database.ExpenseCategories.SingleAsync(x => x.Id == data.ElectricityCategoryId)).IsActive);
            var categories = new Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories.IndexModel(database);
            await categories.OnGetAsync(CancellationToken.None);
            Assert.True(categories.ExpenseCategories.Single(x => x.Id == data.ElectricityCategoryId).IsElectricityCategory);
        });
        using var create = await client.GetAsync("/finance-b/Administration/Finance/Expenses/Create");
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.DoesNotContain($"value=\"{data.ElectricityCategoryId}\"", await create.Content.ReadAsStringAsync());
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] values) => new(
        values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));

    private sealed record TenantData(string Slug, int AssociationId, int Factor, int MemberId, int PlotId, int ChargeTypeId,
        long ChargeId, long PaymentId, long NotificationId, int MeterId, long CommonReadingId, long ExpenseId,
        int ManualCategoryId, int ElectricityCategoryId, long AuditId);

    private sealed class Fixture : IAsyncDisposable
    {
        public PortalWebApplicationFactory Factory { get; } = new(environmentName: "Development", useSqlite: false);
        public Dictionary<string, TenantData> Data { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            try
            {
                await fixture.Factory.ExecuteDbContextAsync(async database =>
                {
                    database.Associations.Add(new Association { Slug = "finance-b", Name = "Finance B" });
                    database.Users.Add(new ApplicationUser { Id = SharedUser, UserName = SharedUser, FirstName = "Shared", LastName = "Member", SecurityStamp = "financial-tests" });
                    await database.SaveChangesAsync();
                });
                foreach (var slug in new[] { "neftyanik", "finance-b" }) await fixture.SeedAsync(slug);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task ExecuteAsync(string slug, Func<IServiceProvider, ApplicationDbContext, Task> action)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.AsNoTracking().SingleAsync(x => x.Slug == slug));
            await action(scope.ServiceProvider, database);
        }

        private Task SeedAsync(string slug) => ExecuteAsync(slug, async (services, database) =>
        {
            var factor = slug == "neftyanik" ? 1 : 3;
            var member = new Member { FullName = "marker-" + slug, ApplicationUserId = SharedUser };
            var plot = new Plot { Number = "same-number", Address = "marker-" + slug };
            var type = new ChargeType { Code = "FINANCIAL-TEST", Name = "marker-" + slug };
            var manual = new ExpenseCategory { Name = "manual-" + slug };
            var electricity = slug == "neftyanik" ? await database.ExpenseCategories.SingleAsync(x => x.Id == ExpenseCategoryIds.ElectricityPayment)
                : new ExpenseCategory { Name = "Electricity B" };
            if (slug != "neftyanik") database.ExpenseCategories.Add(electricity);
            database.AddRange(member, plot, type, manual, new PlotOwnership { Member = member, Plot = plot });
            database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = SharedUser, Role = RoleNames.Member });
            await database.SaveChangesAsync();
            database.SystemSettings.Add(new SystemSetting { Key = "Finance.ElectricityExpenseCategoryId", Value = electricity.Id.ToString() });
            database.SystemSettings.Add(new SystemSetting { Key = "Finance.CashInitialization", Value = JsonSerializer.Serialize(new
            {
                Amount = 500m * factor, AcceptedAt = Date.AddDays(-5), AcceptedFrom = "marker-" + slug, AdvancePaymentsAmount = 10m * factor
            }) });
            var charge = new Charge { PlotId = plot.Id, ChargeTypeId = type.Id, ChargeDate = Date, Amount = 1000m * factor, Description = "marker-" + slug };
            var expense = new Expense { ExpenseCategoryId = manual.Id, ExpenseDate = Date, Amount = 40m * factor, Description = "marker-" + slug, CreatedByUserId = SharedUser };
            database.AddRange(charge, expense);
            await database.SaveChangesAsync();
            var payment = await services.GetRequiredService<IPaymentService>().CreateMemberPaymentAsync(new(member.Id, plot.Id, Date, factor == 1 ? 300m : 3900m, PaymentMethod.Cash, null, "marker-" + slug, SharedUser));
            Assert.True(payment.Succeeded);
            var notification = await services.GetRequiredService<IPaymentNotificationService>().CreateAsync(member.Id, new(200m * factor, PaymentMethod.Cash, "marker-" + slug));
            Assert.True(notification.Succeeded);
            var meters = services.GetRequiredService<IMemberElectricityService>();
            Assert.True((await meters.CreateTariffAsync(new(Date.AddDays(-3), 5m * factor, null, SharedUser))).Succeeded);
            var meter = await meters.CreateMeterWithInitialReadingAsync(new(member.Id, "same-meter-number", "marker-" + slug, true, plot.Id, new[] { plot.Id }, Date.AddDays(-2), 100m * factor, null, 0m, SharedUser));
            Assert.True(meter.Succeeded, meter.ErrorMessage);
            var reading = await meters.CreateReadingAsync(new(meter.MeterId!.Value, Date.AddDays(-1), 100m * factor + 10m, null, SharedUser));
            Assert.True(reading.Succeeded, reading.ErrorMessage);
            Assert.Equal(50m * factor, reading.TotalAmount);
            var common = services.GetRequiredService<IAssociationElectricityService>();
            Assert.True((await common.CreateTariffAsync(new(Date.AddDays(-3), 7m * factor, 3m * factor, SharedUser))).Succeeded);
            Assert.True((await common.CreateInitialReadingAsync(new(Date.AddDays(-2), 1000m * factor, 500m * factor, SharedUser))).Succeeded);
            var commonReading = await common.CreateReadingAsync(new(Date.AddDays(-1), 1000m * factor + 20m, 500m * factor + 10m, SharedUser));
            Assert.True(commonReading.Succeeded, commonReading.ErrorMessage);
            var supplierExpense = await common.CreateExpenseAsync(new(commonReading.ReadingId!.Value, SharedUser));
            Assert.True(supplierExpense.Succeeded, supplierExpense.ErrorMessage);
            var auditId = await database.FinancialAuditLogs.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            Data[slug] = new(slug, database.CurrentAssociationId, factor, member.Id, plot.Id, type.Id, charge.Id, payment.PaymentId!.Value,
                notification.NotificationId!.Value, meter.MeterId.Value, commonReading.ReadingId.Value, expense.Id, manual.Id, electricity.Id, auditId);
        });

        public Task AssertExpectedAsync(string slug) => ExecuteAsync(slug, async (services, database) =>
        {
            var own = Data[slug];
            var expectedPayments = own.Factor == 1 ? 300m : 3900m;
            var page = new FinancePage(database);
            await page.OnGetAsync(CancellationToken.None);
            Assert.Equal(1050m * own.Factor, page.Summary.TotalActiveCharges);
            Assert.Equal(expectedPayments, page.Summary.TotalActivePayments);
            Assert.Equal(1050m * own.Factor - expectedPayments, await database.CalculateActiveBalanceAsync(own.MemberId, Data.Values.Select(x => x.PlotId)));
            var nav = Assert.Single(await database.Plots.SelectFinanceSummary().ToListAsync());
            Assert.Equal(1050m * own.Factor, nav.ActiveChargesTotal);
            Assert.Equal(expectedPayments, nav.ActivePaymentsTotal);
            Assert.Equal(expectedPayments, (await database.LoadActivePaymentTotalsByPlotAsync(Data.Values.Select(x => x.PlotId)))[own.PlotId]);
            Assert.Equal(1, await services.GetRequiredService<IPaymentNotificationService>().GetPendingCountAsync());
            var context = await services.GetRequiredService<IMemberElectricityService>().GetReadingEntryContextAsync(own.MeterId, Date.AddDays(1), 100m * own.Factor + 20m, null);
            Assert.NotNull(context);
            Assert.Equal(10m, context.Consumption);
            Assert.Equal(50m * own.Factor, context.Amount);
        });

        public async Task<string> SnapshotAsync(string slug)
        {
            string result = "";
            await ExecuteAsync(slug, async (services, database) =>
            {
                var page = new FinancePage(database);
                await page.OnGetAsync(CancellationToken.None);
                var tables = new Dictionary<string, object>();
                async Task AddAsync<T>() where T : class
                {
                    var properties = database.Model.FindEntityType(typeof(T))!.GetProperties().OrderBy(x => x.Name).ToArray();
                    var rows = await database.Set<T>().AsNoTracking().ToListAsync();
                    tables[typeof(T).Name] = rows.OrderBy(x => x.GetType().GetProperty("Id")!.GetValue(x)!.ToString(), StringComparer.Ordinal)
                        .Select(x => properties.ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(x))).ToArray();
                }
                await AddAsync<Charge>(); await AddAsync<Payment>(); await AddAsync<PaymentAllocation>(); await AddAsync<PaymentNotification>();
                await AddAsync<Expense>(); await AddAsync<ExpenseCategory>(); await AddAsync<SystemSetting>(); await AddAsync<FinancialAuditLog>();
                await AddAsync<MemberElectricityMeter>(); await AddAsync<MemberElectricityReading>(); await AddAsync<MemberElectricityTariff>();
                await AddAsync<AssociationElectricityReading>(); await AddAsync<AssociationElectricityTariff>();
                await AddAsync<Plot>(); await AddAsync<PlotOwnership>();
                result = JsonSerializer.Serialize(new { page.Summary, page.PlotBalances, Tables = tables,
                    Notifications = await services.GetRequiredService<IPaymentNotificationService>().GetForAdministrationAsync(new(null)),
                    NavigationTotals = await database.Plots.SelectFinanceSummary().ToListAsync() });
            });
            return result;
        }

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
