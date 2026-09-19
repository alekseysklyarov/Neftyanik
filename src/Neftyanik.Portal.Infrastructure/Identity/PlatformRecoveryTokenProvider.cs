using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformRecoveryTokenOptions : DataProtectionTokenProviderOptions
{
    public PlatformRecoveryTokenOptions()
    {
        Name = "DachaHub.PlatformRecovery.v1";
        TokenLifespan = TimeSpan.FromMinutes(15);
    }
}

public sealed class PlatformRecoveryTokenProvider(
    IDataProtectionProvider protection,
    IOptions<PlatformRecoveryTokenOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(protection, options, logger)
{
    public const string ProviderName = "PlatformRecovery";
    public const string ResetPurpose = "PlatformPasswordReset";
}
