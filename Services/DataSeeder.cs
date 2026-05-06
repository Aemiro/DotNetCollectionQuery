using CollectionQuery.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectionQuery.Services
{
    public static class DataSeeder
    {
        public static async Task SeedAsync(BlogDbContext ctx)
        {
            if (ctx == null) return;

            // ensure database is up to date
            await ctx.Database.MigrateAsync();

            // if data exists, skip seeding
            if (await ctx.Categories.AnyAsync()) return;

            var now = DateTime.UtcNow;

            var tech = new Category("Technology", "technology", "Posts about technology");
            var life = new Category("Lifestyle", "lifestyle", "Everyday life and tips");

            ctx.Categories.AddRange(tech, life);
            await ctx.SaveChangesAsync();

            var post1 = new Post(
                "Introducing CollectionQuery",
                "introducing-collectionquery",
                "This is a seeded post about the CollectionQuery project.",
                tech.Id,
                now);

            var post2 = new Post(
                "A Day in Life",
                "a-day-in-life",
                "Short reflections about day-to-day life.",
                life.Id,
                now);

            ctx.Posts.AddRange(post1, post2);
            await ctx.SaveChangesAsync();

            var c1 = new Comment(post1.Id, "Alice", "Nice introduction — thanks!");
            var c2 = new Comment(post1.Id, "Bob", "Looking forward to more updates.");
            var c3 = new Comment(post2.Id, "Charlie", "Great read.");

            ctx.Comments.AddRange(c1, c2, c3);
            await ctx.SaveChangesAsync();
        }
    }
}
