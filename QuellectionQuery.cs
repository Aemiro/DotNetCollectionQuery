using System.Linq.Expressions;
using System.Reflection;

namespace CollectionQuery
{
    /// <summary>
    /// Describes paging, filtering, searching, ordering, projection, and related options for
    /// <see cref="CollectionQueryService.QueryAsync{TEntity}"/>. Typically bound from HTTP query parameters.
    /// </summary>
    public sealed class CollectionQuery
    {
        /// <summary>Maximum number of rows to return after <see cref="Skip"/> (page size). Default handling uses 10 when null.</summary>
        public int? Top { get; set; }
        /// <summary>Number of rows to skip before taking <see cref="Top"/> (offset for paging).</summary>
        public int? Skip { get; set; }

        /// <summary>Ordered list of sort keys. Applied in order using ThenBy semantics after the first key.</summary>
        public List<Order>? OrderBy { get; set; }

        /// <summary>Free-text term combined with <see cref="SearchFrom"/> for a LIKE-based OR search across fields.</summary>
        public string? Search { get; set; }
        /// <summary>Dot-separated property paths on the entity to search when <see cref="Search"/> is set.</summary>
        public List<string>? SearchFrom { get; set; }

        /// <summary>
        /// Conjunctive normal form: each inner list is OR-ed, and each outer list is AND-ed.
        /// Example: <c>[[A,B],[C]]</c> means <c>(A OR B) AND (C)</c>.
        /// </summary>
        public List<List<FilterItem>>? Filter { get; set; }

        /// <summary>EF Core include paths (property names, dot-separated for nested navigations).</summary>
        public List<string>? Includes { get; set; }
        /// <summary>
        /// Property paths to copy into each result row. Keys in the returned dictionaries match these paths.
        /// When null or empty, all public instance properties are projected.
        /// </summary>
        public List<string>? Select { get; set; }

        /// <summary>Property names to deduplicate by before paging (first row per group is kept).</summary>
        public List<string>? GroupBy { get; set; }

        /// <summary>When true, the query returns only a total count and does not materialize a page of items.</summary>
        public bool? Count { get; set; }
        /// <summary>When true, global query filters (such as soft-delete) are ignored for this request.</summary>
        public bool WithArchived { get; set; } = false;

        /// <summary>When true, applies DISTINCT (or <see cref="DistinctOn"/> when set) before paging.</summary>
        public bool? Distinct { get; set; }
        /// <summary>Fields that define uniqueness when <see cref="Distinct"/> is true (server-side when supported).</summary>
        public List<string>? DistinctOn { get; set; }
    }

    /// <summary>Sort direction for <see cref="Order.Direction"/>.</summary>
    public enum Direction
    {
        /// <summary>Ascending order.</summary>
        ASC,
        /// <summary>Descending order.</summary>
        DESC
    }

    /// <summary>One sort key in <see cref="CollectionQuery.OrderBy"/>.</summary>
    public sealed class Order
    {
        /// <summary>Dot-separated public property path on the entity type.</summary>
        public string Field { get; set; } = default!;
        /// <summary>Whether to sort ascending or descending.</summary>
        public Direction Direction { get; set; } = Direction.ASC;
        /// <summary>Optional SQL null ordering hint: <c>"NULLS FIRST"</c> or <c>"NULLS LAST"</c> (provider-dependent).</summary>
        public string? Nulls { get; set; }
    }

    /// <summary>One atomic predicate inside <see cref="CollectionQuery.Filter"/>.</summary>
    public sealed class FilterItem
    {
        /// <summary>Dot-separated public property path on the entity type.</summary>
        public string Field { get; set; } = default!;
        /// <summary>Right-hand value as string (comma-separated for IN/BETWEEN as implemented by the filter builder).</summary>
        public string? Value { get; set; }
        /// <summary>Operator token; use <see cref="FilterOperators"/> for supported literals.</summary>
        public required string Operator { get; set; }
    }

    /// <summary>String tokens for <see cref="FilterItem.Operator"/> understood by <see cref="Extensions.FilterHelper.ApplyFilters{TEntity}"/>.</summary>
    public static class FilterOperators
    {
        /// <summary>Equality.</summary>
        public const string EqualTo = "=";
        /// <summary>Inequality.</summary>
        public const string NotEqualTo = "!=";
        /// <summary>Less than.</summary>
        public const string LessThan = "<";
        /// <summary>Less than or equal.</summary>
        public const string LessThanOrEqualTo = "<=";
        /// <summary>Greater than.</summary>
        public const string GreaterThan = ">";
        /// <summary>Greater than or equal.</summary>
        public const string GreaterThanOrEqualTo = ">=";
        /// <summary>SQL IN list; <see cref="FilterItem.Value"/> is comma-separated.</summary>
        public const string In = "IN";
        /// <summary>SQL NOT IN list; <see cref="FilterItem.Value"/> is comma-separated.</summary>
        public const string NotIn = "NOT IN";
        /// <summary>SQL IS (typed null comparisons).</summary>
        public const string Is = "IS";
        /// <summary>PostgreSQL IS DISTINCT FROM.</summary>
        public const string IsDistinctFrom = "IS DISTINCT FROM";
        /// <summary>PostgreSQL IS NOT DISTINCT FROM.</summary>
        public const string IsNotDistinctFrom = "IS NOT DISTINCT FROM";
        /// <summary>SQL LIKE (pattern semantics depend on provider).</summary>
        public const string Like = "LIKE";
        /// <summary>Case-insensitive LIKE when supported (e.g. ILIKE / citext).</summary>
        public const string ILike = "ILIKE";
        /// <summary>Negated LIKE.</summary>
        public const string NotLike = "NOT LIKE";
        /// <summary>Negated case-insensitive LIKE when supported.</summary>
        public const string NotILike = "NOT ILIKE";
        /// <summary>POSIX regular expression match (~).</summary>
        public const string RegExp = "~";
        /// <summary>Case-insensitive POSIX regular expression match (~*).</summary>
        public const string RegExpCaseInsensitive = "~*";
        /// <summary>Negated POSIX regular expression match (!~).</summary>
        public const string NotRegExp = "!~";
        /// <summary>Negated case-insensitive POSIX regular expression match (!~*).</summary>
        public const string NotRegExpCaseInsensitive = "!~*";
        /// <summary>SQL SIMILAR TO.</summary>
        public const string SimilarTo = "SIMILAR TO";
        /// <summary>SQL NOT SIMILAR TO.</summary>
        public const string NotSimilarTo = "NOT SIMILAR TO";
        /// <summary>Contains operator (provider-specific).</summary>
        public const string Contains = "CONTAINS";
        /// <summary>PostgreSQL array/json <c>&lt;@</c> contained-by.</summary>
        public const string ContainsLeft = "<@";
        /// <summary>PostgreSQL array/json <c>&amp;&amp;</c> overlap.</summary>
        public const string ContainsRight = "&&";
        /// <summary>PostgreSQL array any-containment.</summary>
        public const string ContainsAny = "&<";
        /// <summary>PostgreSQL array all-containment.</summary>
        public const string ContainsAll = "&>";
        /// <summary>PostgreSQL range adjacency <c>-|-</c>.</summary>
        public const string Overlaps = "-|-";
        /// <summary>Logical NOT.</summary>
        public const string Not = "NOT";
        /// <summary>Logical OR.</summary>
        public const string Or = "OR";
        /// <summary>Logical AND.</summary>
        public const string And = "AND";
        /// <summary>SQL ALL quantifier.</summary>
        public const string All = "ALL";
        /// <summary>SQL ANY quantifier.</summary>
        public const string Any = "ANY";
        /// <summary>Negated ANY quantifier.</summary>
        public const string NotAny = "NOT ANY";
        /// <summary>Closed range; value parses as two bounds.</summary>
        public const string Between = "BETWEEN";
        /// <summary>Outside closed range.</summary>
        public const string NotBetween = "NOT BETWEEN";
        /// <summary>IS NULL test (no value required).</summary>
        public const string IsNull = "IS NULL";
        /// <summary>IS NOT NULL test.</summary>
        public const string NotNull = "IS NOT NULL";
    }

    /// <summary>Paged list payload returned by <see cref="CollectionQueryService"/> when <see cref="CollectionQuery.Count"/> is not true.</summary>
    /// <typeparam name="T">Element type of <see cref="Items"/>.</typeparam>
    public sealed class PagedResult<T>
    {
        /// <summary>Rows for the current page.</summary>
        public List<T> Items { get; set; } = new();
        /// <summary>Total number of rows matching the query before <see cref="CollectionQuery.Top"/> / <see cref="CollectionQuery.Skip"/>.</summary>
        public int TotalCount { get; set; }
        /// <summary>1-based page index derived from <c>Skip</c> and <c>Top</c>.</summary>
        public int PageNumber { get; set; }
        /// <summary>Effective page size (same as resolved <c>Top</c>).</summary>
        public int PageSize { get; set; }
    }

    /// <summary>Expression helpers for resolving dot-separated property paths and building key selectors.</summary>
    public static class PathExpr
    {
        private static readonly Dictionary<(Type, string), LambdaExpression> _cache = new();

        /// <summary>
        /// Walks <paramref name="path"/> from <paramref name="param"/> and returns the leaf member access expression.
        /// </summary>
        /// <param name="param">Root parameter (typically the entity).</param>
        /// <param name="path">Dot-separated property path; matching is case-insensitive.</param>
        /// <param name="type">CLR type of the resolved leaf expression.</param>
        /// <returns>Member access chain ending at the requested property.</returns>
        /// <exception cref="ArgumentException">If a segment does not resolve to a public instance property.</exception>
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

        /// <summary>
        /// Builds <c>e =&gt; new object[] { (object)e.Field1, ... }</c> for use as a composite key selector.
        /// </summary>
        /// <param name="entityType">Entity CLR type.</param>
        /// <param name="fields">Property paths (each passed to <see cref="GetPropertyPath"/>).</param>
        /// <returns>Lambda with body <see cref="object"/> array of boxed field values.</returns>
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

        /// <summary>Validates that every path exists on <paramref name="t"/>; throws if any segment is missing.</summary>
        /// <param name="t">Root entity type.</param>
        /// <param name="paths">Dot-separated paths to verify.</param>
        /// <exception cref="ArgumentException">When a property on the path cannot be resolved.</exception>
        public static void EnsureFieldsExist(Type t, IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                var param = Expression.Parameter(t, "x");
                _ = GetPropertyPath(param, p, out var _); // will throw if not found
            }
        }
    }

    /// <summary>Security and provider toggles for <see cref="CollectionQueryService"/>.</summary>
    public sealed class CollectionQueryOptions
    {
        /// <summary>When true, search/filter LIKE operations prefer provider case-insensitive APIs (e.g. ILIKE on PostgreSQL).</summary>
        public bool UseProviderILike { get; init; } = true;
        /// <summary>When true, uses unaccent-aware matching where implemented (requires PostgreSQL <c>unaccent</c> extension).</summary>
        public bool UseUnaccent { get; init; } = false;
        /// <summary>Optional allow-list for <see cref="CollectionQuery.Select"/> paths; enforced when non-null.</summary>
        public HashSet<string>? AllowedSelectFields { get; init; }
        /// <summary>Optional allow-list for <see cref="CollectionQuery.OrderBy"/> field paths (hook for future strict validation).</summary>
        public HashSet<string>? AllowedOrderFields { get; init; }
        /// <summary>Optional allow-list for filter/search field paths (hook for future strict validation).</summary>
        public HashSet<string>? AllowedFilterFields { get; init; }
        /// <summary>Optional allow-list for <see cref="CollectionQuery.Includes"/> paths (hook for future strict validation).</summary>
        public HashSet<string>? AllowedIncludePaths { get; init; }
    }
}