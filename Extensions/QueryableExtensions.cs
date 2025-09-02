using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace CollectionQuery.Extensions
{
    public static class QueryableExtensions
    {
        public static IOrderedQueryable<T> ThenByDirection<T>(
            this IOrderedQueryable<T> src, LambdaExpression key, Direction dir)
        {
            return dir == Direction.DESC
                ? Queryable.ThenByDescending(src, (dynamic)key)
                : Queryable.ThenBy(src, (dynamic)key);
        }

        public static IQueryable<T> IgnoreFilters<T>(this IQueryable<T> source) where T : class
        {
            return source.IgnoreQueryFilters();
        }
    }
}
