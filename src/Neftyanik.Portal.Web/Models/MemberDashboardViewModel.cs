using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Models
{
    public class PlotDto
    {
        public int Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public decimal Area { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class PaymentDto
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Method { get; set; }
        public string? Note { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public class ElectricityChargeDto
    {
        public long Id { get; set; }
        public int PlotId { get; set; }
        public string PlotNumber { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal ConsumptionKwh { get; set; }
        public decimal Amount { get; set; }
        public bool IsPaid { get; set; }
    }

    public class MemberDashboardViewModel
    {
        // User info
        public string UserId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }

        // Owned plots
        public List<PlotDto> Plots { get; set; } = new List<PlotDto>();

        // Debts
        public decimal CurrentElectricityDebt { get; set; }
        public decimal CurrentMembershipDebt { get; set; }

        // Histories
        public List<PaymentDto> Payments { get; set; } = new List<PaymentDto>();
        public List<ElectricityChargeDto> ElectricityCharges { get; set; } = new List<ElectricityChargeDto>();
    }
}
