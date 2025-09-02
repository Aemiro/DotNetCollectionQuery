using CollectionQuery.Extensions;
using Microsoft.EntityFrameworkCore;
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
            var page = await source.Skip(skip).Take(pageSize).ToListAsync(ct);

            // 4️⃣ Project to dynamic dictionary in-memory
            var selected = ProjectToDictionaries(page, typeof(TEntity), query.Select, _options.AllowedSelectFields);

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
                foreach (var include in q.Includes)
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

        private static void ValidateWhitelist(IEnumerable<string> fields, HashSet<string>? allowed, string name)
        {
            if (allowed == null) return;
            foreach (var f in fields)
                if (!allowed.Contains(f))
                    throw new ArgumentException($"{name} '{f}' is not allowed.");
        }

        private static List<IDictionary<string, object?>> ProjectToDictionaries<TEntity>(
            IEnumerable<TEntity> items, Type entityType, List<string>? fields, HashSet<string>? whitelist)
        {
            var result = new List<IDictionary<string, object?>>();

            if (fields == null || fields.Count == 0)
            {
                // fallback: include all public properties
                foreach (var it in items)
                {
                    result.Add(it!.GetType()
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .ToDictionary(p => p.Name, p => p.GetValue(it), StringComparer.OrdinalIgnoreCase));
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
                    dict[path] = current;
                }
                result.Add(dict);
            }

            return result;
        }
    }
}
