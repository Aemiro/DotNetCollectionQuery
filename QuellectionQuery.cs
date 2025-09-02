using System.Linq.Expressions;
using System.Reflection;
namespace CollectionQuery
{
    public sealed class CollectionQuery
    {
        public int? Top { get; set; }
        public int? Skip { get; set; }

        public List<Order>? OrderBy { get; set; }

        public string? Search { get; set; }
        public List<string>? SearchFrom { get; set; }

        // AND-of-OR: outer = AND groups, inner = OR filters
        public List<List<FilterItem>>? Filter { get; set; }

        public List<string>? Includes { get; set; }
        public List<string>? Select { get; set; }

        public string? Locale { get; set; }

        public List<string>? GroupBy { get; set; }

        public bool? Count { get; set; }
        public bool WithArchived { get; set; } = false;

        public bool? Distinct { get; set; }
        public List<string>? DistinctOn { get; set; }
        public int? Cache { get; set; } // placeholder
    }

    public enum Direction { ASC, DESC }

    public sealed class Order
    {
        public string Field { get; set; } = default!;
        public Direction Direction { get; set; } = Direction.ASC;
        // "NULLS FIRST" | "NULLS LAST"
        public string? Nulls { get; set; }
    }

    public sealed class FilterItem
    {
        public string Field { get; set; } = default!;
        public string? Value { get; set; }
        public string Operator { get; set; }
    }

    //public enum FilterOperator
    //{
    //    EqualTo,
    //    NotEqualTo,
    //    LessThan,
    //    LessThanOrEqualTo,
    //    GreaterThan,
    //    GreaterThanOrEqualTo,
    //    In,
    //    NotIn,
    //    Like,
    //    ILike,         // Use provider-specific ILIKE if available (e.g., Npgsql)
    //    Between,
    //    NotBetween,
    //    IsNull,
    //    NotNull,
    //    Any            // alias of IN(values)
    //}
    public static class FilterOperators
    {
        public const string EqualTo = "=";
        public const string NotEqualTo = "!=";
        public const string LessThan = "<";
        public const string LessThanOrEqualTo = "<=";
        public const string GreaterThan = ">";
        public const string GreaterThanOrEqualTo = ">=";
        public const string In = "IN";
        public const string NotIn = "NOT IN";
        public const string Is = "IS";
        public const string IsDistinctFrom = "IS DISTINCT FROM";
        public const string IsNotDistinctFrom = "IS NOT DISTINCT FROM";
        public const string Like = "LIKE";
        public const string ILike = "ILIKE";
        public const string NotLike = "NOT LIKE";
        public const string NotILike = "NOT ILIKE";
        public const string RegExp = "~";
        public const string RegExpCaseInsensitive = "~*";
        public const string NotRegExp = "!~";
        public const string NotRegExpCaseInsensitive = "!~*";
        public const string SimilarTo = "SIMILAR TO";
        public const string NotSimilarTo = "NOT SIMILAR TO";
        public const string Contains = "CONTAINS";
        public const string ContainsLeft = "<@";
        public const string ContainsRight = "&&";
        public const string ContainsAny = "&<";
        public const string ContainsAll = "&>";
        public const string Overlaps = "-|-";
        public const string Not = "NOT";
        public const string Or = "OR";
        public const string And = "AND";
        public const string All = "ALL";
        public const string Any = "ANY";
        public const string NotAny = "NOT ANY";
        public const string Between = "BETWEEN";
        public const string NotBetween = "NOT BETWEEN";
        public const string IsNull = "IS NULL";
        public const string NotNull = "IS NOT NULL";
    }

    public sealed class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }  // computed
        public int PageSize { get; set; }    // computed
    }

    // Helpers(reflection / expressions + validation)

    public static class PathExpr
    {
        // Cache property chains: (entityType, path) -> Lambda x => x.Foo.Bar
        private static readonly Dictionary<(Type, string), LambdaExpression> _cache = new();

        public static Expression GetPropertyPath(ParameterExpression param, string path, out Type type)
        {
            Expression current = param;
            type = param.Type;

            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var prop = type.GetProperty(segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                    ?? throw new ArgumentException($"Property '{segment}' not found on '{type.Name}'.");
                current = Expression.Property(current, prop);
                type = prop.PropertyType;
            }
            return current;
        }

        public static LambdaExpression GetKeySelector(Type entityType, IEnumerable<string> fields)
        {
            var param = Expression.Parameter(entityType, "x");
            var elems = new List<Expression>();
            foreach (var f in fields)
            {
                var access = GetPropertyPath(param, f, out var _);
                elems.Add(Expression.Convert(access, typeof(object)));
            }
            var array = Expression.NewArrayInit(typeof(object), elems);
            return Expression.Lambda(array, param);
        }

        public static void EnsureFieldsExist(Type t, IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                var param = Expression.Parameter(t, "x");
                _ = GetPropertyPath(param, p, out var _); // will throw if not found
            }
        }
    }
    // Service(all the goodies in one place)

    public sealed class CollectionQueryOptions
    {
        public bool UseProviderILike { get; init; } = true;  // now default true for Npgsql
        public bool UseUnaccent { get; init; } = false;      // requires EXTENSION unaccent
       // Optional whitelists (recommended when accepting user input)
        public HashSet<string>? AllowedSelectFields { get; init; }
        public HashSet<string>? AllowedOrderFields { get; init; }
        public HashSet<string>? AllowedFilterFields { get; init; }
        public HashSet<string>? AllowedIncludePaths { get; init; }

    }

    
}