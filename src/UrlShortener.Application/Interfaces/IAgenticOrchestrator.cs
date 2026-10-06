using System.Threading.Tasks;

namespace UrlShortener.Application.Interfaces
{
    public interface IAgenticOrchestrator
    {
        Task<string> ProcessRequirementAsync(string requirement);
    }
}
