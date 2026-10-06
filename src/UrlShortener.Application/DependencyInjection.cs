using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UrlShortener.Application.Common;
using UrlShortener.Application.Workflows;

namespace UrlShortener.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
            
            // Bind settings from appsettings.json
            services.Configure<UrlShortenerSettings>(options =>
            {
                options.BaseShortUrl = configuration["UrlShortenerSettings:BaseShortUrl"] ?? "http://localhost:5000/";
            });
            services.AddSingleton(resolver =>
                resolver.GetRequiredService<Microsoft.Extensions.Options.IOptions<UrlShortenerSettings>>().Value);

            services.AddSingleton<IPromptSanitizer, PromptSanitizer>();
            services.AddScoped<IEngineeringWorkflowService, EngineeringWorkflowService>();

            return services;
        }
    }
}
