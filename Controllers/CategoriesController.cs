using CollectionQuery.Dtos;
using CollectionQuery.Models;
using CollectionQuery.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CollectionQuery.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(BlogDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync();
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var category = await db.Categories.FindAsync(id);
        return category is null ? NotFound() : Ok(category);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CategoryCreateDto dto)
    {
        var category = new Category(dto.Name, SlugService.Generate(dto.Name), dto.Description);
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CategoryUpdateDto dto)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null) return NotFound();
        category.SetName(dto.Name);
        category.SetDescription(dto.Description);
        category.SetSlug(SlugService.Generate(dto.Name));

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null) return NotFound();
        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

