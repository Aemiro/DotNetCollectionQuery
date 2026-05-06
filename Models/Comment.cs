using System.ComponentModel.DataAnnotations;

namespace CollectionQuery.Models
{
    public class Comment : BaseEntity
    {
        public Guid PostId { get; private set; }
        public virtual Post Post { get; private set; } = default!;
        [Required, MaxLength(120)]
        public string AuthorName { get; private set; } = default!;
        [Required, MaxLength(4000)]
        public string Body { get; private set; } = default!;
        // setter methods are private to enforce immutability after creation
        public void SetPostId(Guid postId) => PostId = postId;
        public void SetAuthorName(string authorName) => AuthorName = authorName;
        public void SetBody(string body) => Body = body;
        public Comment()
        {
            this.Id = Guid.NewGuid();
        }
        public Comment(Guid postId, string authorName, string body): base()
        {
            this.SetPostId(postId);
            this.SetAuthorName(authorName);
            this.SetBody(body);
        }
    }
}
