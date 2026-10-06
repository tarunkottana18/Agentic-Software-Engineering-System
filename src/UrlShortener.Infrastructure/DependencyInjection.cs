using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Repositories;
using UrlShortener.Infrastructure.ShortCodes;
using UrlShortener.Core.Interfaces;
using UrlShortener.Application.Common;
using UrlShortener.Application.Workflows;
using UrlShortener.Infrastructure.WorkflowArtifacts;

namespace UrlShortener.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<UrlDbContext>(options =>
                options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

            services.AddScoped<IUrlRepository, UrlRepository>();
            services.AddScoped<IEngineeringWorkflowRepository, EngineeringWorkflowRepository>();
            services.AddSingleton<IShortCodeGenerator, CryptographicShortCodeGenerator>();
            services.AddSingleton<IWorkflowArtifactStore, FileWorkflowArtifactStore>();
            services.AddSingleton<IWorkspaceChangeTracker, FileWorkspaceChangeTracker>();
            services.AddSingleton<ITrustedValidationExecutor, TrustedValidationExecutor>();

            return services;
        }
    }
}
