using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Demo;

public static class DemoData
{
    public static DbContextOptions<ApplicationDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString)
            .ReplaceService<IModelCustomizer, DemoModelCustomizer>().Options;

    public static async Task<Association> SeedAsync(string connectionString, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var associationContext = new AssociationContext();
        await using var db = new ApplicationDbContext(Options(connectionString), associationContext);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var association = await db.Associations.SingleAsync(cancellationToken);
        association.Name = "Тестове товариство / Тестовое товарищество";
        association.Slug = "demo";
        association.ContactEmail = null;
        association.ContactPhone = null;
        associationContext.Resolve(association);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var start = new DateOnly(today.Year, 1, 1);
        var roles = new[] { RoleNames.Member, RoleNames.Accountant, RoleNames.Administrator };
        foreach (var role in roles)
        {
            var id = PublicDemo.UserId(role);
            db.Users.Add(new ApplicationUser
            {
                Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(),
                FirstName = "Демо", LastName = role == RoleNames.Member ? "Учасник" : role == RoleNames.Accountant ? "Бухгалтер" : "Адміністратор",
                DisplayName = role == RoleNames.Member ? "Демо: учасник" : role == RoleNames.Accountant ? "Демо: бухгалтер" : "Демо: адміністратор",
                SecurityStamp = Guid.NewGuid().ToString("N"), IsActive = true
            });
            db.AssociationUserMemberships.Add(new AssociationUserMembership { AssociationId = association.Id, ApplicationUserId = id, Role = role });
        }
        await db.SaveChangesAsync(cancellationToken);
        var electricity = new ChargeType { AssociationId = association.Id, Name = "Електроенергія", Code = ChargeTypeCodes.Electricity, IsActive = true };
        var membership = new ChargeType { AssociationId = association.Id, Name = "Членські внески", IsMembershipFee = true, IsYearly = true, DefaultAmount = 1200, IsActive = true };
        db.ChargeTypes.AddRange(electricity, membership);
        await db.SaveChangesAsync(cancellationToken);
        var members = new List<Member>();
        var plots = new List<Plot>();
        for (var i = 0; i < 6; i++)
        {
            var member = new Member { AssociationId = association.Id, FullName = $"Тестовий учасник {i + 1:00}", JoinedAt = start, ApplicationUserId = i == 0 ? PublicDemo.UserId(RoleNames.Member) : null };
            var plot = new Plot { AssociationId = association.Id, Number = (i + 1).ToString("00"), AreaSquareMeters = 600, Address = $"Навчальна вулиця, {i + 1}" };
            db.Members.Add(member);
            db.Plots.Add(plot);
            db.PlotOwnerships.Add(new PlotOwnership { AssociationId = association.Id, Member = member, Plot = plot, ValidFrom = start, IsPrimaryContact = true, OwnershipShare = 100 });
            members.Add(member);
            plots.Add(plot);
        }
        await db.SaveChangesAsync(cancellationToken);
        var rate = new MemberElectricityTariff { AssociationId = association.Id, EffectiveFrom = start, Rate = 4.32m };
        db.MemberElectricityTariffs.Add(rate);
        db.SystemSettings.Add(new SystemSetting { AssociationId = association.Id, Key = "Finance.ElectricityExpenseCategoryId", Value = "1" });
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var plot = plots[i];
            var meter = new MemberElectricityMeter { AssociationId = association.Id, Member = member, BillingPlot = plot, MeterNumber = $"DEMO-{i + 1:000}", Name = $"Лічильник ділянки {plot.Number}" };
            db.MemberElectricityMeters.Add(meter);
            await db.SaveChangesAsync(cancellationToken);
            plot.MemberElectricityMeterId = meter.Id;
            var fee = new Charge { AssociationId = association.Id, MemberId = member.Id, PlotId = plot.Id, ChargeType = membership, Amount = 1200, ChargeDate = today.AddDays(-30), DueDate = today.AddDays(15), PeriodYear = today.Year, Description = "Навчальне нарахування членських внесків" };
            var charge = new Charge { AssociationId = association.Id, MemberId = member.Id, PlotId = plot.Id, ChargeType = electricity, Amount = (80 + i * 10) * 4.32m, ChargeDate = today.AddDays(-5), PeriodYear = today.Year, PeriodMonth = today.Month };
            db.Charges.AddRange(fee, charge);
            db.MemberElectricityReadings.Add(new MemberElectricityReading { AssociationId = association.Id, MemberElectricityMeter = meter, ReadingDate = today.AddDays(-35), CurrentReading = 1000, IsInitialReading = true });
            db.MemberElectricityReadings.Add(new MemberElectricityReading { AssociationId = association.Id, MemberElectricityMeter = meter, ReadingDate = today.AddDays(-5), CurrentReading = 1080 + i * 10, AppliedMemberRate = 4.32m, Amount = charge.Amount, Charge = charge });
            var paid = i % 2 == 0 ? 1200m : 800m;
            var payment = new Payment { AssociationId = association.Id, MemberId = member.Id, PlotId = plot.Id, Amount = paid, PaymentDate = today.AddDays(-10), PaymentMethod = PaymentMethod.Cash, CreatedByUserId = PublicDemo.UserId(RoleNames.Accountant), Description = "Навчальний платіж", BalanceBeforePayment = 1200, BalanceAfterPayment = 1200 - paid };
            db.Payments.Add(payment);
            db.PaymentAllocations.Add(new PaymentAllocation { AssociationId = association.Id, Payment = payment, Charge = fee, Amount = paid });
        }
        db.Expenses.Add(new Expense { AssociationId = association.Id, ExpenseCategoryId = 2, ExpenseDate = today.AddDays(-3), Amount = 500, Description = "Навчальний приклад витрат на охорону", FundingSource = 1, CreatedByUserId = PublicDemo.UserId(RoleNames.Accountant) });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return association;
    }
}

// Provider-specific conversions apply only to ephemeral demo databases, never SQL Server.
public sealed class DemoModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder builder, DbContext context)
    {
        base.Customize(builder, context);
        foreach (var entity in builder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero)));
                // SQLite's decimal storage cannot support SQL aggregates/orderings used by these pages.
                // This conversion is confined to disposable sample data; production keeps exact decimals.
                else if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                    property.SetValueConverter(new ValueConverter<decimal, double>(value => (double)value, value => (decimal)value));
    }
}
