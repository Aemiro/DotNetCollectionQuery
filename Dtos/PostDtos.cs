namespace CollectionQuery.Dtos
{
    public record PostCreateDto(string Title, string Content, Guid CategoryId, bool Publish);
    public record PostUpdateDto(string Title, string Content, Guid CategoryId, bool Publish);
    public class PostDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        public Guid CategoryId { get; set; }

        // Category as a DTO object (safe, no back reference to Posts)
        public CategoryDto Category { get; set; } = default!;

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<CommentDto> Comments { get; set; } = new();
    }

    public class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class CommentDto
    {
        public Guid Id { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
