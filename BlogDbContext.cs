using CollectionQuery.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CollectionQuery
{

    public class BlogDbContext(DbContextOptions<BlogDbContext> options) : DbContext(options)
    {
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Post> Posts => Set<Post>();
        public DbSet<Comment> Comments => Set<Comment>();

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            foreach (var e in ChangeTracker.Entries<BaseEntity>())
            {
                if (e.State == EntityState.Added)
                {
                    e.Entity.CreatedAt = now;
                    e.Entity.UpdatedAt = now;
                }
                else if (e.State == EntityState.Modified)
                {
                    e.Entity.UpdatedAt = now;
                }
            }
            return base.SaveChangesAsync(ct);
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Category>().HasIndex(x => x.Slug).IsUnique();
            b.Entity<Post>().HasIndex(x => x.Slug).IsUnique();
        }
    }
}