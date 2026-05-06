using System.Linq.Expressions;
using System.Reflection;

namespace CollectionQuery.Extensions
{
    /// <summary>EF-translatable projection to string-keyed dictionaries.</summary>
    public static class DynamicSelectExtensions
    {
        /// <summary>
        /// Builds an expression tree that constructs a <see cref="Dictionary{TKey,TValue}"/> per row with the requested keys.
        /// </summary>
        /// <typeparam name="TEntity">Source entity type.</typeparam>
        /// <param name="source">Query to project.</param>
        /// <param name="fields">Property paths (dot-separated); null means all public instance properties on <typeparamref name="TEntity"/>.</param>
        /// <param name="allowedFields">Optional intersection filter for field names.</param>
        /// <returns>Queryable of dictionaries (still composable until executed).</returns>
        public static IQueryable<IDictionary<string, object?>> SelectDynamic<TEntity>(
            this IQueryable<TEntity> source,
            IEnumerable<string> fields,
            ISet<string>? allowedFields = null)
        {
            var parameter = Expression.Parameter(typeof(TEntity), "e");

            var selectedFields = fields?.ToList() ?? typeof(TEntity).GetProperties().Select(p => p.Name).ToList();
            if (allowedFields is { Count: > 0 })
                selectedFields = selectedFields.Where(f => allowedFields.Contains(f)).ToList();

            var addMethod = typeof(Dictionary<string, object?>).GetMethod("Add")!;
            var dictVar = Expression.Variable(typeof(Dictionary<string, object?>), "dict");
            var blockExpressions = new List<Expression> { Expression.Assign(dictVar, Expression.New(typeof(Dictionary<string, object?>))) };

            foreach (var field in selectedFields)
            {
                var property = GetNestedProperty(parameter, field);
                if (property == null) continue;

                var value = Expression.Convert(property, typeof(object));
                var addCall = Expression.Call(dictVar, addMethod, Expression.Constant(field), value);
                blockExpressions.Add(addCall);
            }

            blockExpressions.Add(dictVar);
            var body = Expression.Block(new[] { dictVar }, blockExpressions);
            var selector = Expression.Lambda<Func<TEntity, IDictionary<string, object?>>>(body, parameter);

            return source.Select(selector);
        }

        private static Expression? GetNestedProperty(Expression param, string fieldPath)
        {
            Expression current = param;
            foreach (var part in fieldPath.Split('.'))
            {
                var prop = current.Type.GetProperty(part, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return null;
                current = Expression.Property(current, prop);
            }
            return current;
        }
    }
}
