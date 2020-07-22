using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Models
{
    public class ShortUrl
    {
        public int Id { get; set; }
        [Required]
        public string BrandName { get; set; }
        [Required]
        public string UserID { get; set; }
        [Required]
        public string BoardID { get; set; }
        public string OriginalUrl { get; set; }
    }
}
