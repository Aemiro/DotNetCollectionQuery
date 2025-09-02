using CollectionQuery.Dtos;
using CollectionQuery.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CollectionQuery.Controllers
{

    [ApiController]
    [Route("api/posts/{postId:guid}/[controller]")]
    public class CommentsController(BlogDbContext db) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetForPost(Guid postId)
        {
            if (!await db.Posts.AnyAsync(p => p.Id == postId))
                return NotFound();
            var comments = await db.Comments
                .Where(c => c.PostId == postId)
                .AsNoTracking()
                .ToListAsync();
            return Ok(comments);
        }

        [HttpPost]
        public async Task<IActionResult> Create(Guid postId, CommentCreateDto dto)
        {
            if (!await db.Posts.AnyAsync(p => p.Id == postId))
                return NotFound();
            var comment = new Comment(postId, dto.AuthorName, dto.Body);
            db.Comments.Add(comment);
            await db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetForPost), new { postId }, comment);
        }
    }

}
