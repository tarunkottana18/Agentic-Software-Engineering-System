using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Interfaces;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Repositories
{
    public class UrlRepository : IUrlRepository
    {
        private readonly UrlDbContext _context;

        public UrlRepository(UrlDbContext context)
        {
            _context = context;
        }

        public async Task<UrlMapping?> GetByShortCodeAsync(string shortCode)
        {
            return await _context.UrlMappings
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode);
        }

        public async Task<UrlMapping?> GetByOriginalUrlAsync(string originalUrl)
        {
            return await _context.UrlMappings
                .FirstOrDefaultAsync(u => u.OriginalUrl == originalUrl);
        }

        public async Task AddAsync(UrlMapping urlMapping)
        {
            await _context.UrlMappings.AddAsync(urlMapping);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsShortCodeConflict(exception))
            {
                _context.Entry(urlMapping).State = EntityState.Detached;
                throw new ShortCodeConflictException(urlMapping.ShortCode, exception);
            }
        }

        public async Task UpdateAsync(UrlMapping urlMapping)
        {
            _context.UrlMappings.Update(urlMapping);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> IncrementClickCountAsync(string shortCode)
        {
            var updated = await _context.UrlMappings
                .Where(u => u.ShortCode == shortCode)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.ClickCount, u => u.ClickCount + 1));
            return updated > 0;
        }

        private static bool IsShortCodeConflict(DbUpdateException exception)
        {
            return exception.GetBaseException() is SqliteException sqliteException
                && sqliteException.SqliteErrorCode == 19
                && sqliteException.Message.Contains("UrlMappings.ShortCode", StringComparison.OrdinalIgnoreCase);
        }
    }
}
