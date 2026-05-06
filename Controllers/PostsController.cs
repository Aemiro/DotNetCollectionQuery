using CollectionQuery.Dtos;
using CollectionQuery.Models;
using CollectionQuery.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CollectionQuery.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PostsController : ControllerBase
    {
        private readonly BlogDbContext _db;
        private readonly CollectionQueryService _svc;
        public PostsController(BlogDbContext db)
        {
            _db = db;
            _svc = new CollectionQueryService(new CollectionQueryOptions
            {
                UseProviderILike = true,
                UseUnaccent = true, // set true only if CREATE EXTENSION unaccent;
                AllowedIncludePaths = new(StringComparer.OrdinalIgnoreCase)
                //{
                //    "Category", "Comments"
                //},
                //    AllowedFilterFields = new(StringComparer.OrdinalIgnoreCase)
                //{
                //    "Title","Description","Category.Name"
                //},
                //    AllowedOrderFields = new(StringComparer.OrdinalIgnoreCase)
                //{
                //    "CreatedAt","Description","Category.Name"
                //},
                //    AllowedSelectFields = new(StringComparer.OrdinalIgnoreCase)
                //{
                //    "Id","Name","Description","Category.Name"
                //}
            });
        }
        [HttpGet]
        public async Task<ActionResult<PagedResult<PostDto>>> GetAll([FromQuery()] CollectionQuery query)
        {
            query.OrderBy = query.OrderBy ?? [new Order { Field = "CreatedAt", Direction = Direction.DESC }];
            var result = await _svc.QueryAsync(_db.Posts, query);

            return Ok(result);
        }


        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var post = await _db.Posts
                .Include(p => p.Category)
                .Include(p => p.Comments)
                .FirstOrDefaultAsync(p => p.Id == id);
            return post is null ? NotFound() : Ok(post);
        }

        [HttpPost]
        public async Task<IActionResult> Create(PostCreateDto dto)
        {
            if (!await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId))
                return BadRequest("Invalid category");
            var post = new Post(dto.Title, SlugService.Generate(dto.Title), dto.Content, dto.CategoryId,
                dto.Publish ? DateTime.UtcNow : null);
            _db.Posts.Add(post);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = post.Id }, post);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, PostUpdateDto dto)
        {
            var post = await _db.Posts.FindAsync(id);
            if (post is null) return NotFound();
            post.SetTitle(dto.Title);
            post.SetContent(dto.Content);
            post.SetCategoryId(dto.CategoryId);
            post.SetSlug(SlugService.Generate(dto.Title));
            post.SetPublishedAt(dto.Publish ? DateTime.UtcNow : null);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var post = await _db.Posts.FindAsync(id);
            if (post is null) return NotFound();
            _db.Posts.Remove(post);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }

}