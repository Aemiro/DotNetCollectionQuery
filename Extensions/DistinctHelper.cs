

using System.Linq.Expressions;

namespace CollectionQuery.Extensions
{
    public static class DistinctHelper
    {
        public static IQueryable<TEntity> ApplyDistinctOn<TEntity>(IQueryable<TEntity> source, IEnumerable<string> fields)
            where TEntity : class
        {
            if (fields == null || !fields.Any()) return source;

            PathExpr.EnsureFieldsExist(typeof(TEntity), fields);
            var selector = (Expression<Func<TEntity, object[]>>)PathExpr.GetKeySelector(typeof(TEntity), fields);
            return source.GroupBy(selector).Select(g => g.First());
        }
    }
}
