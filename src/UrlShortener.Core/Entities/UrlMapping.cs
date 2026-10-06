using System;
using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Core.Entities
{
    public class UrlMapping
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public string OriginalUrl { get; set; } = string.Empty;
        
        [Required]
        public string ShortCode { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public int ClickCount { get; set; } = 0;
    }
}
