using System;
using System.Threading.Tasks;
using UrlShortener.Core.Entities;

namespace UrlShortener.Core.Interfaces
{
    public interface IUrlRepository
    {
        Task<UrlMapping?> GetByShortCodeAsync(string shortCode);
        Task<UrlMapping?> GetByOriginalUrlAsync(string originalUrl);
        Task AddAsync(UrlMapping urlMapping);
        Task UpdateAsync(UrlMapping urlMapping);
        Task<bool> IncrementClickCountAsync(string shortCode);
    }
}
