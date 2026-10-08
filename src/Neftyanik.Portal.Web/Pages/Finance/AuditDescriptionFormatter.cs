using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Finance;

public static class AuditDescriptionFormatter
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Regex Reference = new(@"(?<label>уведомлени[ея] о плат[её]же|плат[её]ж(?:а)?|начислени[ея]|(?:электро)?сч[её]тчик(?:а)?|показани[ея])\s+#(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private sealed record Context(int? MemberId, int? PlotId, string? Detail = null);

    // Enrich a page at a time. Original audit descriptions and snapshots remain unchanged.
    public static async Task<IReadOnlyDictionary<long, string?>> FormatAsync(ApplicationDbContext db,
        IReadOnlyList<FinancialAuditLog> entries, CancellationToken ct = default)
    {
        var keys = new HashSet<(string Type, long Id)>();
        foreach (var entry in entries)
        {
            if (long.TryParse(entry.EntityId, out var id)) keys.Add((entry.EntityType, id));
            foreach (Match match in Reference.Matches(entry.Description ?? ""))
                if (long.TryParse(match.Groups["id"].Value, out id)) keys.Add((TypeOf(match.Groups["label"].Value), id));
        }
        long[] Ids(string type) => keys.Where(k => k.Type == type).Select(k => k.Id).ToArray();
        var contexts = new Dictionary<(string Type, long Id), Context>();
        var ids = Ids(nameof(Payment));
        if (ids.Length > 0)
            foreach (var p in await db.Payments.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.MemberId, p.PlotId }).ToListAsync(ct))
                contexts[(nameof(Payment), p.Id)] = new(p.MemberId, p.PlotId);
        ids = Ids(nameof(Charge));
        if (ids.Length > 0)
            foreach (var c in await db.Charges.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.MemberId, c.PlotId, Name = c.ChargeType == null ? null : c.ChargeType.Name }).ToListAsync(ct))
                contexts[(nameof(Charge), c.Id)] = new(c.MemberId, c.PlotId, c.Name);
        ids = Ids(nameof(PaymentNotification));
        if (ids.Length > 0)
            foreach (var n in await db.PaymentNotifications.AsNoTracking().Where(n => ids.Contains(n.Id)).Select(n => new { n.Id, n.MemberId, PlotId = n.Payment == null ? null : n.Payment.PlotId }).ToListAsync(ct))
                contexts[(nameof(PaymentNotification), n.Id)] = new(n.MemberId, n.PlotId);
        ids = Ids(nameof(MemberElectricityMeter));
        if (ids.Length > 0)
            foreach (var m in await db.MemberElectricityMeters.AsNoTracking().Where(m => ids.Contains(m.Id)).Select(m => new { m.Id, m.MemberId, m.BillingPlotId, m.Name }).ToListAsync(ct))
                contexts[(nameof(MemberElectricityMeter), m.Id)] = new(m.MemberId, m.BillingPlotId, m.Name);
        ids = Ids(nameof(MemberElectricityReading));
        if (ids.Length > 0)
            foreach (var r in await db.MemberElectricityReadings.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new
            {
                r.Id,
                MemberId = r.Charge != null ? r.Charge.MemberId : r.MemberElectricityMeter == null ? (int?)null : r.MemberElectricityMeter.MemberId,
                PlotId = r.Charge != null ? r.Charge.PlotId : r.MemberElectricityMeter == null ? (int?)null : r.MemberElectricityMeter.BillingPlotId
            }).ToListAsync(ct))
                contexts[(nameof(MemberElectricityReading), r.Id)] = new(r.MemberId, r.PlotId);

        var snapshots = entries.ToDictionary(e => e.Id, e => Read(e.NewValuesJson));
        var oldSnapshots = entries.ToDictionary(e => e.Id, e => Read(e.OldValuesJson));
        var memberIds = contexts.Values.Select(c => c.MemberId).Concat(snapshots.Values.Select(s => Integer(s, "MemberId")))
            .Concat(oldSnapshots.Values.Select(s => Integer(s, "MemberId"))).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var plotIds = contexts.Values.Select(c => c.PlotId).Concat(snapshots.Values.Select(s => Integer(s, "PlotId")))
            .Concat(oldSnapshots.Values.Select(s => Integer(s, "PlotId"))).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var members = await db.Members.AsNoTracking().Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.FullName, ct);
        var plots = await db.Plots.AsNoTracking().Where(p => plotIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);
        var result = new Dictionary<long, string?>();
        foreach (var entry in entries)
        {
            var snapshot = snapshots[entry.Id];
            var old = oldSnapshots[entry.Id];
            var advance = Text(snapshot, "Source") == "Advance";
            var primaryId = long.TryParse(entry.EntityId, out var parsedId) ? parsedId : 0;
            string? Describe(string type, long id)
            {
                var primary = type == entry.EntityType && id == primaryId;
                contexts.TryGetValue((type, id), out var context);
                var memberId = primary ? Integer(snapshot, "MemberId") ?? Integer(old, "MemberId") ?? context?.MemberId : context?.MemberId;
                var plotId = primary ? Integer(snapshot, "PlotId") ?? Integer(old, "PlotId") ?? context?.PlotId : context?.PlotId;
                var parts = new List<string>();
                if (memberId.HasValue && members.TryGetValue(memberId.Value, out var name)) parts.Add(name);
                if (plotId.HasValue && plots.TryGetValue(plotId.Value, out var number)) parts.Add("участок " + number);
                if (context?.Detail is { Length: > 0 } detail) parts.Add(detail);
                // Amounts always come from the event snapshot, never from today's mutable balance/payment.
                if (primary && !advance)
                {
                    var amount = Number(snapshot, "Amount");
                    var previous = Number(old, "Amount");
                    if (amount.HasValue)
                        parts.Add(previous.HasValue && previous != amount ? $"{Money(previous.Value)} → {Money(amount.Value)}" : Money(amount.Value));
                    else if (previous.HasValue) parts.Add(Money(previous.Value));
                    if (type == nameof(MemberElectricityReading))
                    {
                        var value = Number(snapshot, "Value") ?? Number(snapshot, "CurrentReading");
                        if (value.HasValue) parts.Add("Т1 " + value.Value.ToString("0.###", Russian) + " кВт·ч");
                        var night = Number(snapshot, "CurrentNightReading");
                        if (night.HasValue) parts.Add("Т2 " + night.Value.ToString("0.###", Russian) + " кВт·ч");
                    }
                }
                return parts.Count == 0 ? null : string.Join(" · ", parts);
            }
            var replacedPrimary = false;
            var description = Reference.Replace(entry.Description ?? "", match =>
            {
                if (!long.TryParse(match.Groups["id"].Value, out var id)) return match.Value;
                var type = TypeOf(match.Groups["label"].Value);
                var detail = Describe(type, id);
                if (detail is null) return match.Value;
                if (type == entry.EntityType && id == primaryId) replacedPrimary = true;
                return match.Groups["label"].Value + " («" + detail + "»)";
            });
            if (!replacedPrimary && primaryId > 0 && Describe(entry.EntityType, primaryId) is { } primaryDetail)
                description += " — " + primaryDetail;
            if (advance && Number(snapshot, "Amount") is { } allocated)
                description += " Сумма зачёта: " + Money(allocated) + ".";
            result[entry.Id] = string.IsNullOrWhiteSpace(description) ? entry.Description : description;
        }
        return result;
    }

    private static string TypeOf(string label)
    {
        var text = label.ToLowerInvariant().Replace('ё', 'е');
        if (text.StartsWith("уведомлен")) return nameof(PaymentNotification);
        if (text.StartsWith("плат")) return nameof(Payment);
        if (text.StartsWith("начислен")) return nameof(Charge);
        if (text.StartsWith("показан")) return nameof(MemberElectricityReading);
        return nameof(MemberElectricityMeter);
    }
    private static string Money(decimal value) => value.ToString("0.00", Russian) + " грн";
    private static JsonElement Read(string? json)
    {
        try { using var document = JsonDocument.Parse(json ?? "null"); return document.RootElement.Clone(); }
        catch (JsonException) { return default; }
    }
    private static JsonElement Property(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) ? value : default;
    private static int? Integer(JsonElement json, string name) => Property(json, name) is var value && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
    private static decimal? Number(JsonElement json, string name) => Property(json, name) is var value && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : null;
    private static string? Text(JsonElement json, string name) => Property(json, name) is var value && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
