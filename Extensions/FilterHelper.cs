using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace CollectionQuery.Extensions
{
    public static class FilterHelper
    {
        public static IQueryable<TEntity> ApplyFilters<TEntity>(
            IQueryable<TEntity> source, List<List<FilterItem>> andGroups, bool useProviderILike) where TEntity : class
        {
            var param = Expression.Parameter(typeof(TEntity), "x");
            Expression? final = null;

            foreach (var orGroup in andGroups)
            {
                Expression? orExpr = null;
                foreach (var f in orGroup)
                {
                    var left = PathExpr.GetPropertyPath(param, f.Field, out var memberType);
                    var expr = f.Operator switch
                    {
                        FilterOperators.IsNull => Expression.Equal(
                        left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) == null
                            ? Expression.Convert(left, typeof(object))
                            : left,
                        Expression.Constant(null, typeof(object))
                    ),
                        FilterOperators.NotNull => Expression.NotEqual(
                            left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) == null
                                ? Expression.Convert(left, typeof(object))
                                : left,
                            Expression.Constant(null, typeof(object))
                        ),
                        FilterOperators.Between or FilterOperators.NotBetween => BuildBetween(left, memberType, f),
                        FilterOperators.In or FilterOperators.NotIn => BuildIn(left, memberType, f),
                        FilterOperators.Like or FilterOperators.ILike => BuildLike(left, memberType, f),
                        _ => BuildBinary(left, memberType, f)  // for =, !=, <, <=, >, >=
                    };

                    orExpr = orExpr is null ? expr : Expression.OrElse(orExpr, expr);
                }
                final = final is null ? orExpr! : Expression.AndAlso(final, orExpr!);
            }

            if (final != null)
            {
                var lambda = Expression.Lambda<Func<TEntity, bool>>(final, param);
                source = source.Where(lambda);
            }
            return source;
        }

        public static IQueryable<TEntity> ApplySearch<TEntity>(
            IQueryable<TEntity> source, IEnumerable<string> fields, string term, bool providerILike)
            where TEntity : class
        {
            var param = Expression.Parameter(typeof(TEntity), "x");
            Expression? or = null;
            var ef = Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!);

            foreach (var f in fields)
            {
                var access = PathExpr.GetPropertyPath(param, f, out _);
                var asString = access.Type == typeof(string) ? access : Expression.Call(access, nameof(object.ToString), Type.EmptyTypes);
                var likeMethod = typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like), new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;
                var pattern = Expression.Constant($"%{term}%");
                var expr = Expression.Call(likeMethod, ef, asString, pattern);
                or = or is null ? expr : Expression.OrElse(or, expr);
            }

            return or == null ? source : source.Where(Expression.Lambda<Func<TEntity, bool>>(or, param));
        }

        public static IQueryable<TEntity> ApplyOrdering<TEntity>(IQueryable<TEntity> source, List<Order> orders)
    where TEntity : class
        {
            IOrderedQueryable<TEntity>? ordered = null;

            foreach (var ord in orders)
            {
                var param = Expression.Parameter(typeof(TEntity), "x");
                var body = PathExpr.GetPropertyPath(param, ord.Field, out var propType);

                // Create Expression<Func<TEntity, TProperty>> dynamically
                var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), propType);
                var lambda = Expression.Lambda(delegateType, body, param);

                string methodName;
                if (ordered == null)
                {
                    methodName = ord.Direction == Direction.DESC ? "OrderByDescending" : "OrderBy";
                    ordered = (IOrderedQueryable<TEntity>)typeof(Queryable)
                        .GetMethods()
                        .Single(m => m.Name == methodName && m.GetParameters().Length == 2)
                        .MakeGenericMethod(typeof(TEntity), propType)
                        .Invoke(null, new object[] { source, lambda })!;
                }
                else
                {
                    methodName = ord.Direction == Direction.DESC ? "ThenByDescending" : "ThenBy";
                    ordered = (IOrderedQueryable<TEntity>)typeof(Queryable)
                        .GetMethods()
                        .Single(m => m.Name == methodName && m.GetParameters().Length == 2)
                        .MakeGenericMethod(typeof(TEntity), propType)
                        .Invoke(null, new object[] { ordered, lambda })!;
                }
            }

            return ordered ?? source;
        }
        private static Expression BuildBinary(Expression left, Type leftType, FilterItem f)
        {
            var (constant, ct) = ToConstant(leftType, f.Value);

            // Map string operator to ExpressionType
            var op = f.Operator switch
            {
                FilterOperators.EqualTo => ExpressionType.Equal,
                FilterOperators.NotEqualTo => ExpressionType.NotEqual,
                FilterOperators.LessThan => ExpressionType.LessThan,
                FilterOperators.LessThanOrEqualTo => ExpressionType.LessThanOrEqual,
                FilterOperators.GreaterThan => ExpressionType.GreaterThan,
                FilterOperators.GreaterThanOrEqualTo => ExpressionType.GreaterThanOrEqual,
                _ => throw new NotSupportedException($"Unsupported operator {f.Operator}")
            };

            return Expression.MakeBinary(op, Promote(left, ct), constant);
        }
        private static (Expression, Expression) ReadTwo(Type t, object? val)
        {
            object? first = null, second = null;

            if (val is IEnumerable<object?> seq)
            {
                var arr = seq.Take(2).ToArray(); // Take at most 2 elements
                if (arr.Length > 0) first = arr[0];
                if (arr.Length > 1) second = arr[1];
            }
            else
            {
                var s = Convert.ToString(val) ?? "";
                var parts = s.Split(',', 2);
                first = parts.ElementAtOrDefault(0);
                second = parts.ElementAtOrDefault(1);
            }

            return (ToConstant(t, first).Item1, ToConstant(t, second).Item1);
        }
        private static Expression BuildIn(Expression left, Type leftType, FilterItem f)
        {
            // Split the comma-separated string into values
            var vals = (f.Value ?? string.Empty)
                       .Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(v => ChangeType(v.Trim(), Nullable.GetUnderlyingType(leftType) ?? leftType))
                       .ToList();

            // Create a constant expression for the list
            var listConst = Expression.Constant(vals);

            // Generate Enumerable.Contains call
            var elemType = Nullable.GetUnderlyingType(leftType) ?? leftType;
            return Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                new[] { elemType },
                listConst,
                Promote(left, elemType)
            );
        }

        private static Expression BuildLike(Expression left, Type leftType, FilterItem f)
        {
            var pattern = $"%{Convert.ToString(f.Value) ?? ""}%";
            var asString = left.Type == typeof(string) ? left : Expression.Call(left, nameof(object.ToString), Type.EmptyTypes);
            var ef = Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!);
            var likeMethod = typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like),
                                    new[] { typeof(DbFunctions), typeof(string), typeof(string) })!;
            return Expression.Call(likeMethod, ef, asString, Expression.Constant(pattern));
        }

        private static (ConstantExpression, Type) ToConstant(Type targetType, object? value)
        {
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return (Expression.Constant(ChangeType(value, t), t), t);
        }

        private static object? ChangeType(object? v, Type t)
        {
            if (v is null) return null;
            if (t.IsAssignableFrom(v.GetType())) return v;
            if (t.IsEnum) return Enum.Parse(t, v.ToString()!, true);
            return Convert.ChangeType(v, t);
        }
        public static IQueryable<TEntity> ApplyDistinctOn<TEntity>(
    this IQueryable<TEntity> source,
    List<string>? distinctOn)
        {
            if (distinctOn == null || distinctOn.Count == 0)
                return source;

            // Single-field distinct can be translated to SQL
            if (distinctOn.Count == 1)
            {
                var field = distinctOn[0];
                source = source
                    .GroupBy(x => EF.Property<object>(x, field))
                    .Select(g => g.First());
            }
            else
            {
                // Multi-field distinct: client-side evaluation
                source = source
                    .AsEnumerable() // switch to client-side
                    .GroupBy(x =>
                    {
                        var type = x.GetType();
                        return distinctOn.Select(f =>
                        {
                            var prop = type.GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            if (prop == null)
                                throw new ArgumentException($"Property '{f}' not found on type '{type.Name}'");
                            return prop.GetValue(x);
                        }).ToArray();
                    })
                    .Select(g => g.First())
                    .AsQueryable();
            }

            return source;
        }
        public static IQueryable<TEntity> ApplyGroupBy<TEntity>(IQueryable<TEntity> source, IEnumerable<string> fields)
        {
            if (fields == null || !fields.Any())
                return source;

            var fieldList = fields.ToList();

            // Single-field: can be translated to SQL
            if (fieldList.Count == 1)
            {
                var param = Expression.Parameter(typeof(TEntity), "x");
                var body = PathExpr.GetPropertyPath(param, fieldList[0], out _);
                var selector = Expression.Lambda<Func<TEntity, object>>(Expression.Convert(body, typeof(object)), param);
                return source.GroupBy(selector).Select(g => g.First());
            }

            // Multi-field: EF Core cannot translate object[] GroupBy, evaluate on client
            var paramMulti = Expression.Parameter(typeof(TEntity), "x");
            var keys = fieldList.Select(f => PathExpr.GetPropertyPath(paramMulti, f, out _)).ToArray();
            var selectorMulti = Expression.Lambda<Func<TEntity, object[]>>(Expression.NewArrayInit(typeof(object), keys), paramMulti);

            return source.AsEnumerable()                  // switch to client-side
                         .GroupBy(selectorMulti.Compile())
                         .Select(g => g.First())
                         .AsQueryable();
        }

        private static Expression Promote(Expression left, Type to) => left.Type == to ? left : Expression.Convert(left, to);

        // Project entire object to dictionary
        public static List<Dictionary<string, object?>> ProjectToDictionaries<T>(
            IEnumerable<T> source,
            List<string>? includeFields = null,
            HashSet<string>? allowedFields = null)
        {
            var props = typeof(T).GetProperties()
                .Where(p => allowedFields == null || allowedFields.Contains(p.Name))
                .ToList();

            if (includeFields != null && includeFields.Count > 0)
            {
                props = props.Where(p => includeFields.Contains(p.Name)).ToList();
            }

            var list = new List<Dictionary<string, object?>>();
            foreach (var item in source)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var p in props)
                {
                    dict[p.Name] = p.GetValue(item);
                }
                list.Add(dict);
            }

            return list;
        }
        // Dynamic projection: select only requested fields
        public static List<Dictionary<string, object?>> SelectDynamic<T>(
            IEnumerable<T> source,
            List<string> selectFields,
            HashSet<string>? allowedFields = null)
        {
            var fields = selectFields
                .Where(f => allowedFields == null || allowedFields.Contains(f))
                .ToList();

            var list = new List<Dictionary<string, object?>>();

            foreach (var item in source)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var f in fields)
                {
                    var prop = typeof(T).GetProperty(f);
                    if (prop != null)
                    {
                        dict[f] = prop.GetValue(item);
                    }
                }
                list.Add(dict);
            }

            return list;
        }

        private static Expression BuildBetween(Expression left, Type leftType, FilterItem f)
        {
            var (low, high) = ReadTwo(leftType, f.Value);
            var greaterThanOrEqual = Expression.GreaterThanOrEqual(left, low);
            var lessThanOrEqual = Expression.LessThanOrEqual(left, high);
            return Expression.AndAlso(greaterThanOrEqual, lessThanOrEqual);
        }

        private static Expression BuildNotBetween(Expression left, Type leftType, FilterItem f)
        {
            var (low, high) = ReadTwo(leftType, f.Value);
            var lessThan = Expression.LessThan(left, low);
            var greaterThan = Expression.GreaterThan(left, high);
            return Expression.OrElse(lessThan, greaterThan);
        }
        private static Expression BuildNotIn(Expression left, Type leftType, FilterItem f)
        {
            // Convert the value into a list of items
            var vals = (f.Value?.ToString() ?? "")
                       .Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(v => ChangeType(v, Nullable.GetUnderlyingType(leftType) ?? leftType))
                       .ToArray();

            if (!vals.Any()) return Expression.Constant(true); // nothing to exclude

            var listConst = Expression.Constant(vals);
            var containsCall = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains),
                                               new[] { Nullable.GetUnderlyingType(leftType) ?? leftType },
                                               listConst, Promote(left, Nullable.GetUnderlyingType(leftType) ?? leftType));

            return Expression.Not(containsCall);
        }

    }
}