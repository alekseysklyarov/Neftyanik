using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Models;

namespace Neftyanik.Portal.Web.Controllers
{
    [Authorize(Roles = RoleNames.Member)]
    public class MemberController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;

        public MemberController(UserManager<ApplicationUser> userManager, ApplicationDbContext db)
        {
            _userManager = userManager;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Challenge();

            var userCharges = await _db.Charges
                .AsNoTracking()
                .Include(c => c.PaymentAllocations)
                .Where(c => c.UserId == user.Id && c.Status == ChargeStatus.Active)
                .OrderByDescending(c => c.ChargedAt)
                .ToListAsync();

            var model = new MemberDashboardViewModel
            {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber
            };

            var plots = await _db.Plots.Where(p => p.OwnerId == user.Id).ToListAsync();
            var plotIds = plots.Select(p => p.Id).ToList();

            model.Plots = plots.Select(p => new PlotDto
            {
                Id = p.Id,
                Number = p.Number,
                Area = p.Area,
                Status = p.Status.ToString()
            }).ToList();

            model.CurrentElectricityDebt = userCharges
                .Where(c => c.ChargeType == ChargeType.Electricity)
                .Sum(c => c.Amount - c.PaymentAllocations.Sum(a => a.Amount));

            var year = DateTime.UtcNow.Year;
            model.CurrentMembershipDebt = userCharges
                .Where(c => c.ChargeType == ChargeType.MembershipFee && c.PeriodYear == year)
                .Sum(c => c.Amount - c.PaymentAllocations.Sum(a => a.Amount));

            var payments = await _db.Payments
                .AsNoTracking()
                .Where(p => p.UserId == user.Id)
                .OrderByDescending(p => p.PaymentDate)
                .Take(50)
                .ToListAsync();

            model.Payments = payments.Select(p => new PaymentDto
            {
                Id = p.Id,
                Amount = p.Amount,
                Date = p.Date,
                Method = p.Method,
                Note = p.Note,
                Source = p.Source.ToString()
            }).ToList();

            var charges = userCharges
                .Where(c => c.ChargeType == ChargeType.Electricity && c.PlotId.HasValue && plotIds.Contains(c.PlotId.Value))
                .Take(100)
                .ToList();

            var plotMap = plots.ToDictionary(p => p.Id, p => p.Number);

            model.ElectricityCharges = charges.Select(c => new ElectricityChargeDto
            {
                Id = c.Id,
                PlotId = c.PlotId ?? 0,
                PlotNumber = c.PlotId.HasValue && plotMap.TryGetValue(c.PlotId.Value, out var plotNumber) ? plotNumber : string.Empty,
                PeriodStart = c.PeriodMonth.HasValue
                    ? new DateTime(c.PeriodYear, c.PeriodMonth.Value, 1)
                    : new DateTime(c.PeriodYear, 1, 1),
                PeriodEnd = c.PeriodMonth.HasValue
                    ? new DateTime(c.PeriodYear, c.PeriodMonth.Value, DateTime.DaysInMonth(c.PeriodYear, c.PeriodMonth.Value))
                    : new DateTime(c.PeriodYear, 12, 31),
                ConsumptionKwh = c.Quantity ?? 0m,
                Amount = c.Amount,
                IsPaid = c.Amount <= c.PaymentAllocations.Sum(a => a.Amount)
            }).ToList();

            return View(model);
        }
    }
}
