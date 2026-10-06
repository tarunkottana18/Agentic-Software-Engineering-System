using UrlShortener.Core.Entities;

namespace UrlShortener.Core.Interfaces
{
    public interface IEngineeringWorkflowRepository
    {
        Task<EngineeringWorkflowRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
        Task<IReadOnlyList<EngineeringWorkflowRun>> GetAllAsync(CancellationToken cancellationToken);
        Task AddAsync(EngineeringWorkflowRun workflow, CancellationToken cancellationToken);
        Task UpdateAsync(EngineeringWorkflowRun workflow, CancellationToken cancellationToken);
    }
}