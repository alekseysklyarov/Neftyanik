using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Interfaces;
using Neftyanik.Portal.Application.LegacyImport;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Neftyanik.Portal.Infrastructure.LegacyImport;
using Neftyanik.Portal.Infrastructure.Repositories;
using Neftyanik.Portal.Infrastructure.Services;

namespace Neftyanik.Portal.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            services.Configure<LegacyElectricityImportOptions>(configuration.GetSection(LegacyElectricityImportOptions.SectionName));

            // Repositories
            services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
            services.AddScoped<IAssociationElectricityService, AssociationElectricityService>();
            services.AddScoped<IMemberElectricityService, MemberElectricityService>();
            services.AddScoped<LegacyElectricityWorkbookReader>();
            services.AddScoped<LegacyElectricityImportValidator>();
            services.AddScoped<ILegacyElectricityImportExecutionHook, NoOpLegacyElectricityImportExecutionHook>();
            services.AddScoped<ILegacyElectricityImportService, LegacyElectricityDataImportService>();

            return services;
        }
    }
}
