using Microsoft.AspNetCore.Identity;

namespace Neftyanik.Portal.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public string? DisplayName { get; set; }

    public bool MustChangePassword { get; set; }

    public List<Member> Members { get; set; } = [];

    public List<PlotOwnershipHistory> PlotOwnershipHistoryRecords { get; set; } = [];

    public List<MeterReading> SubmittedMeterReadings { get; set; } = [];

    public List<MeterReading> ApprovedMeterReadings { get; set; } = [];

    public List<Expense> CreatedExpenses { get; set; } = [];

    public List<NewsArticle> CreatedNewsArticles { get; set; } = [];

    public List<AssociationDocument> UploadedDocuments { get; set; } = [];

    public List<AuditLog> AuditLogs { get; set; } = [];

    public List<ElectricityMeter> ElectricityMeters { get; set; } = [];

    public List<Charge> CreatedCharges { get; set; } = [];

    public List<ElectricityReading> CreatedElectricityReadings { get; set; } = [];

    public List<ElectricityTariff> CreatedElectricityTariffs { get; set; } = [];

    public List<AssociationElectricityReading> CreatedAssociationElectricityReadings { get; set; } = [];

    public List<AssociationElectricityTariff> CreatedAssociationElectricityTariffs { get; set; } = [];

    public List<MemberElectricityMeter> CreatedMemberElectricityMeters { get; set; } = [];

    public List<MemberElectricityReading> CreatedMemberElectricityReadings { get; set; } = [];

    public List<MemberElectricityTariff> CreatedMemberElectricityTariffs { get; set; } = [];

    public List<Payment> CreatedPayments { get; set; } = [];

    public List<SystemSetting> UpdatedSystemSettings { get; set; } = [];
}
