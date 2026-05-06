using CollectionQuery.Extensions;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Reflection;

namespace CollectionQuery
{
    public class CollectionQueryService
    {
        private readonly CollectionQueryOptions _options;

        public CollectionQueryService(CollectionQueryOptions? options = null)
        {
            _options = options ?? new CollectionQueryOptions();
        }

        public async Task<object> QueryAsync<TEntity>(
            IQueryable<TEntity> source,
            CollectionQuery query,
            CancellationToken ct = default) where TEntity : class
        {
            // 1️⃣ Apply filters, search, includes, ordering, distinct
            source = ApplyAll(source, query);
            var total = await source.CountAsync(ct);

            // Count only
            if (query.Count == true)
            {
                return new { Count = total };
            }
            var pageSize = Math.Max(1, query.Top ?? 10);
            var skip = query.Skip ?? 0;
            var pageNumber = (skip / pageSize) + 1;

            // 3️⃣ Fetch page
            // if the includes count more than 2 the use AsSplitQuery to avoid cartesian explosion, but it will execute one query per include, so use it only when necessary
                if (query.Includes != null && query.Includes.Count > 2)
                {
                    source = source.AsSplitQuery();
                }
                var page = await source.Skip(skip).Take(pageSize).ToListAsync(ct);
            // var page = await source.Skip(skip).Take(pageSize).ToListAsync(ct);

            // 4️⃣ Project to dynamic dictionary in-memory
            var preserveBackReferences = IncludesPastEfTruncation(typeof(TEntity), query.Includes);
            var selected = ProjectToDictionaries(page, typeof(TEntity), query.Select, _options.AllowedSelectFields, preserveBackReferences);

            return new PagedResult<IDictionary<string, object?>>
            {
                Items = selected,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        // ---------- Internals ----------

        private IQueryable<TEntity> ApplyAll<TEntity>(IQueryable<TEntity> source, CollectionQuery q) where TEntity : class
        {
            if (q.WithArchived) source = source.IgnoreQueryFilters();

            if (q.Includes?.Count > 0)
            {
                foreach (var include in FilterEfIncludePaths<TEntity>(q.Includes))
                {
                    var propName = char.ToUpper(include[0]) + include.Substring(1); // capitalize first letter
                    source = source.Include(propName);
                }
            }

            if (q.Filter?.Count > 0)
            {
                var allFilterFields = q.Filter.SelectMany(g => g.Select(f => f.Field)).Distinct();
                //ValidateWhitelist(allFilterFields, _options.AllowedFilterFields, "filter field");
                source = FilterHelper.ApplyFilters(source, q.Filter, _options.UseProviderILike);
            }

            if (!string.IsNullOrWhiteSpace(q.Search) && q.SearchFrom?.Count > 0)
            {
                //ValidateWhitelist(q.SearchFrom, _options.AllowedFilterFields, "search field");
                source = FilterHelper.ApplySearch(source, q.SearchFrom, q.Search!, _options.UseProviderILike);
            }

            if (q.Distinct == true && q.DistinctOn?.Count > 0)
            {
                source = source.ApplyDistinctOn(q.DistinctOn);
            }
            else if (q.Distinct == true)
            {
                source = source.Distinct();
            }
            if (q.GroupBy?.Count > 0)
            {
                source = FilterHelper.ApplyGroupBy(source, q.GroupBy);
            }

            if (q.OrderBy?.Count > 0)
            {
                //ValidateWhitelist(q.OrderBy.Select(o => o.Field), _options.AllowedOrderFields, "order field");
                source = FilterHelper.ApplyOrdering(source, q.OrderBy);
            }

            return source;
        }

        /// <summary>
        /// Rewrites include paths that EF Core will ignore and then treat as an error
        /// (<c>NavigationBaseIncludeIgnored</c>): paths that walk a reference navigation back to the query root
        /// are truncated to the last valid prefix (see <see cref="NormalizeEfIncludePath"/>).
        /// </summary>
        private static IEnumerable<string> FilterEfIncludePaths<TEntity>(List<string> includes) where TEntity : class
        {
            var root = typeof(TEntity);
            foreach (var path in includes)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var normalized = NormalizeEfIncludePath(root, path.Trim());
                if (normalized.Length == 0)
                    continue;
                yield return normalized;
            }
        }

        /// <summary>
        /// Truncates before the first segment that is a reference navigation back to the query root (after the first hop).
        /// </summary>
        private static string NormalizeEfIncludePath(Type rootEntityType, string path)
        {
            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return string.Empty;

            Type current = rootEntityType;
            for (var i = 0; i < segments.Length; i++)
            {
                var prop = current.GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null)
                    return path;

                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var isCollection = propType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propType);
                var nextType = isCollection ? GetEnumerableElementType(propType) : propType;
                if (nextType == null)
                    return path;

                if (i > 0 && !isCollection && nextType == rootEntityType)
                    return string.Join(".", segments.Take(i));

                current = nextType;
            }

            return path;
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }

            return null;
        }

        /// <summary>
        /// True when any requested include path extends past the segment where <see cref="NormalizeEfIncludePath"/>
        /// truncates for EF (client asked for navigations EF will not materialize via Include).
        /// </summary>
        private static bool IncludesPastEfTruncation(Type rootEntityType, List<string>? includes)
        {
            if (includes == null)
                return false;
            foreach (var raw in includes)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var full = raw.Trim();
                var norm = NormalizeEfIncludePath(rootEntityType, full);
                if (norm.Length == 0)
                    continue;
                if (!IncludePathsEqualBySegments(full, norm))
                    return true;
            }
            return false;
        }

        private static bool IncludePathsEqualBySegments(string a, string b)
        {
            var sa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var sb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (sa.Length != sb.Length)
                return false;
            for (var i = 0; i < sa.Length; i++)
            {
                if (!sa[i].Equals(sb[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Materializes collections whose element type has a reference navigation to the query root.
        /// When <paramref name="preserveBackReferences"/> is false, omits that navigation (EF fix-up noise).
        /// When true (include path past EF truncation), includes it as a scalar-only snapshot (primitives, strings,
        /// enums, FKs) so nested graphs like Category are not duplicated on the principal.
        /// </summary>
        private static object? MaterializeValueForJson(object? value, Type rootEntityType, bool preserveBackReferences)
        {
            if (value == null)
                return value;
            if (value is string)
                return value;
            if (value is not IEnumerable en)
                return value;
            var et = GetEnumerableElementType(value.GetType());
            if (et == null || !ElementTypeHasReferenceNavigationToRoot(et, rootEntityType))
                return value;
            var list = new List<IDictionary<string, object?>>();
            foreach (var item in en)
            {
                if (item == null)
                    continue;
                if (preserveBackReferences)
                    list.Add(DependentWithNestedPrincipalScalarsOnly(item, rootEntityType));
                else
                    list.Add(ElementToDictionaryOmitRootBackRefs(item, rootEntityType));
            }
            return list;
        }

        private static Dictionary<string, object?> DependentWithNestedPrincipalScalarsOnly(object item, Type rootEntityType)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var isCollection = pt != typeof(string) && typeof(IEnumerable).IsAssignableFrom(pt);
                if (!isCollection && pt == rootEntityType)
                {
                    var principal = prop.GetValue(item);
                    dict[prop.Name] = principal == null ? null : EntityToScalarOnlyDictionary(principal);
                    continue;
                }
                dict[prop.Name] = prop.GetValue(item);
            }
            return dict;
        }

        /// <summary>
        /// Public properties suitable for JSON leaf values: excludes class/collection navigations (e.g. Category, Comments).
        /// </summary>
        private static Dictionary<string, object?> EntityToScalarOnlyDictionary(object entity)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsJsonScalarProperty(prop))
                    continue;
                dict[prop.Name] = prop.GetValue(entity);
            }
            return dict;
        }

        private static bool IsJsonScalarProperty(PropertyInfo prop)
        {
            var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (t == typeof(string) || t == typeof(byte[]))
                return true;
            if (t.IsEnum)
                return true;
            if (t.IsPrimitive || t == typeof(decimal))
                return true;
            if (t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly))
                return true;
            if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t))
                return false;
            if (t.IsClass)
                return false;
            if (t.IsValueType)
                return true;
            return false;
        }

        private static bool ElementTypeHasReferenceNavigationToRoot(Type elementType, Type rootEntityType)
        {
            foreach (var prop in elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (pt == typeof(string))
                    continue;
                if (typeof(IEnumerable).IsAssignableFrom(pt))
                    continue;
                if (pt == rootEntityType)
                    return true;
            }
            return false;
        }

        private static Dictionary<string, object?> ElementToDictionaryOmitRootBackRefs(object item, Type rootEntityType)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (pt != typeof(string) && typeof(IEnumerable).IsAssignableFrom(pt))
                {
                    dict[prop.Name] = prop.GetValue(item);
                    continue;
                }
                if (pt == rootEntityType)
                    continue;
                dict[prop.Name] = prop.GetValue(item);
            }
            return dict;
        }

        private static void ValidateWhitelist(IEnumerable<string> fields, HashSet<string>? allowed, string name)
        {
            if (allowed == null) return;
            foreach (var f in fields)
                if (!allowed.Contains(f))
                    throw new ArgumentException($"{name} '{f}' is not allowed.");
        }

        private static List<IDictionary<string, object?>> ProjectToDictionaries<TEntity>(
            IEnumerable<TEntity> items, Type entityType, List<string>? fields, HashSet<string>? whitelist, bool preserveBackReferences)
        {
            var result = new List<IDictionary<string, object?>>();

            if (fields == null || fields.Count == 0)
            {
                // fallback: include all public properties
                foreach (var it in items)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in it!.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var v = prop.GetValue(it);
                        row[prop.Name] = MaterializeValueForJson(v, entityType, preserveBackReferences);
                    }
                    result.Add(row);
                }
                return result;
            }

            if (whitelist != null)
            {
                foreach (var f in fields)
                    if (!whitelist.Contains(f))
                        throw new ArgumentException($"select '{f}' is not allowed.");
            }

            foreach (var it in items)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in fields)
                {
                    object? current = it;
                    foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (current == null) break;
                        var p = current.GetType().GetProperty(seg, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        current = p?.GetValue(current);
                    }
                    dict[path] = MaterializeValueForJson(current, entityType, preserveBackReferences);
                }
                result.Add(dict);
            }

            return result;
        }
    }
}
