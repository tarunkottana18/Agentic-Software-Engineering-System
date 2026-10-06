using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Interfaces;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Repositories
{
    public sealed class EngineeringWorkflowRepository : IEngineeringWorkflowRepository
    {
        private readonly UrlDbContext _context;

        public EngineeringWorkflowRepository(UrlDbContext context)
        {
            _context = context;
        }

        public async Task<EngineeringWorkflowRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return await _context.EngineeringWorkflowRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(workflow => workflow.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<EngineeringWorkflowRun>> GetAllAsync(CancellationToken cancellationToken)
        {
            return await _context.EngineeringWorkflowRuns
                .AsNoTracking()
                .OrderByDescending(workflow => workflow.CreatedAtUtc)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(EngineeringWorkflowRun workflow, CancellationToken cancellationToken)
        {
            await _context.EngineeringWorkflowRuns.AddAsync(workflow, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(EngineeringWorkflowRun workflow, CancellationToken cancellationToken)
        {
            var trackedWorkflow = _context.EngineeringWorkflowRuns.Local
                .SingleOrDefault(item => item.Id == workflow.Id);
            if (trackedWorkflow is not null)
            {
                _context.Entry(trackedWorkflow).State = EntityState.Detached;
            }

            var entry = _context.EngineeringWorkflowRuns.Update(workflow);
            entry.Property(item => item.Revision).OriginalValue = workflow.Revision - 1;
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                entry.State = EntityState.Detached;
                throw new WorkflowRevisionConflictException(exception);
            }
        }
    }
}