using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CollectionQuery.Models
{
    public class Category : BaseEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; private set; } = default!;
        [Required, MaxLength(140)]
        public string Slug { get; private set; } = default!;
        [MaxLength(1000)]
        public string? Description { get; private set; }
        [JsonIgnore] // <-- prevent back-reference
        public List<Post> Posts { get; set; } = [];
        // setter methods are private to enforce immutability after creation
        public void SetName(string name) => Name = name;
        public void SetSlug(string slug) => Slug = slug;
        public void SetDescription(string? description) => Description = description;
        public Category()
        {
            this.Id = Guid.NewGuid();
        }
        public Category(string name, string slug, string? description = null): base()
        {
            this.SetName(name);
            this.SetSlug(slug);
            this.SetDescription(description);
        }
    }
}
