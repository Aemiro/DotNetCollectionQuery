

using System.Linq.Expressions;

namespace CollectionQuery.Extensions
{
    /// <summary>Server-friendly DISTINCT ON via <c>GroupBy</c> + <c>First</c>.</summary>
    public static class DistinctHelper
    {
        /// <summary>
        /// Returns one arbitrary row per distinct combination of <paramref name="fields"/> (validated against <typeparamref name="TEntity"/>).
        /// </summary>
        /// <typeparam name="TEntity">Entity type.</typeparam>
        /// <param name="source">Source query.</param>
        /// <param name="fields">Property paths forming the uniqueness key.</param>
        /// <returns>Deduplicated queryable.</returns>
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
