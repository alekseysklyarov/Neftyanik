namespace Neftyanik.Portal.Domain.Entities;

public class MemberElectricityMeterPlot
{
    public int MemberElectricityMeterId { get; set; }

    public MemberElectricityMeter? MemberElectricityMeter { get; set; }

    public int PlotId { get; set; }

    public Plot? Plot { get; set; }
}
