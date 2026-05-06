using System.ComponentModel.DataAnnotations;

namespace CollectionQuery.Models
{
    public class Post : BaseEntity
    {
        [Required, MaxLength(200)]
        public string Title { get; private set; } = default!;
        [Required, MaxLength(220)]
        public string Slug { get; private set; } = default!;
        [Required]
        public string Content { get; private set; } = default!;
        public Guid CategoryId { get; private set; }
        //[JsonIgnore] // <-- prevent back-reference
        public virtual Category Category { get; set; } = default!;
        public DateTime? PublishedAt { get; private set; }
        public virtual ICollection<Comment> Comments { get; set; } = [];
        // setter methods are private to enforce immutability after creation
        public void SetTitle(string title) => Title = title;
        public void SetSlug(string slug) => Slug = slug;
        public void SetContent(string content) => Content = content;
        public void SetCategoryId(Guid categoryId) => CategoryId = categoryId;
        public void SetPublishedAt(DateTime? publishedAt) => PublishedAt = publishedAt;
        public Post()
        {
            this.Id = Guid.NewGuid();
        }
        public Post(string title, string slug, string content, Guid categoryId, DateTime? publishedAt = null): base()
        {
            this.SetTitle(title);
            this.SetSlug(slug);
            this.SetContent(content);
            this.SetCategoryId(categoryId);
            this.SetPublishedAt(publishedAt);
        }
    }
}
