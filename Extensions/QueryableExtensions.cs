using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace CollectionQuery.Extensions
{
    /// <summary>Small <see cref="IQueryable{T}"/> helpers related to collection query behavior.</summary>
    public static class QueryableExtensions
    {
        /// <summary>Applies LINQ <c>ThenBy</c> or <c>ThenByDescending</c> based on <paramref name="dir"/>.</summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="src">Already ordered query.</param>
        /// <param name="key">Key selector expression.</param>
        /// <param name="dir">Ascending or descending.</param>
        /// <returns>Further ordered queryable.</returns>
        public static IOrderedQueryable<T> ThenByDirection<T>(
            this IOrderedQueryable<T> src, LambdaExpression key, Direction dir)
        {
            return dir == Direction.DESC
                ? Queryable.ThenByDescending(src, (dynamic)key)
                : Queryable.ThenBy(src, (dynamic)key);
        }

        /// <summary>Alias for <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}"/>.</summary>
        public static IQueryable<T> IgnoreFilters<T>(this IQueryable<T> source) where T : class
        {
            return source.IgnoreQueryFilters();
        }
    }
}
