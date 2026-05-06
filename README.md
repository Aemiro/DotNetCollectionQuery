# Collection Query for .NET

Turn rich **HTTP query strings** into **Entity Framework Core** queries, then return **paged rows** as `IDictionary<string, object?>` for JSON APIs—without OData.

**Important:** Filters, search, sorting, includes, and distinct/group run on the `IQueryable` in the database (where EF can translate them). The **`select`** list is applied **after** your page is loaded from SQL. Details are in [Request lifecycle](#request-lifecycle).

---

## Quick navigation

- [Overview](#overview)
- [When to use this](#when-to-use-this)
- [Capabilities](#capabilities)
- [Request lifecycle](#request-lifecycle)
- [Requirements](#requirements)
- [Backend (ASP.NET Core)](#backend-aspnet-core)
- [Frontend (query strings)](#frontend-query-strings)
- [Query cheat sheet](#query-cheat-sheet)
- [Filtering](#filtering)
- [API responses](#api-responses)
- [Limitations](#limitations)
- [Source layout](#source-layout)
- [License](#license)

---

## Overview

| Question | Answer |
|----------|--------|
| **What it does** | Binds a `CollectionQuery` model (usually from `[FromQuery]`) and runs it through `CollectionQueryService.QueryAsync<TEntity>()`. |
| **Stack** | .NET 8+, ASP.NET Core, EF Core (sample uses EF Core 9 + Npgsql). |
| **Typical output** | Either a paged list of dictionary rows, or `{ "count": N }` when `count=true`. |
| **Try it in this repo** | `GET /api/posts` with query parameters, or **Swagger UI** in the Development environment. |

---

## When to use this

1. **Grids and admin tools** — You want `top`, `skip`, `orderBy`, `filter`, `search`, and optional `select` without adopting OData.
2. **Dynamic columns** — Callers choose which fields appear in each response; you still keep one endpoint and one EF entity type.
3. **One orchestration point** — `CollectionQueryService` applies includes, filters, search, distinct/group, sort, total count, paging, then projection.

---

## Capabilities

| Topic | What you get |
|--------|----------------|
| **Paging** | `top` (default 10, minimum 1), `skip`, plus `pageNumber` / `pageSize` / `totalCount` on the result. |
| **Sorting** | Multiple `orderBy` keys; first is primary sort, rest are `ThenBy`. Paths are case-insensitive and can use dots (`Category.Name`). |
| **Search** | One `search` term matched with `LIKE` across every `searchFrom` path (combined with OR). |
| **Filters** | [CNF](#filtering): OR inside each group, AND between groups. |
| **Includes** | EF `Include` per path; split query when there are **more than two** includes. |
| **Projection** | `select` paths become dictionary keys; omit `select` to serialize all public properties. |
| **Count only** | `count=true` returns a total with no item list. |
| **Soft delete** | `withArchived=true` maps to `IgnoreQueryFilters()`. |
| **Distinct / group** | `distinct`, `distinctOn`, `groupBy` (see [Limitations](#limitations) for multi-field behavior). |
| **Hardening** | `CollectionQueryOptions.AllowedSelectFields` is enforced; other allow-lists are optional—you should still validate public APIs. |

---

## Request lifecycle

**Phase 1 — Composing the query (`ApplyAll`)**

The service walks your `CollectionQuery` in this **fixed order**:

1. `withArchived` → optional `IgnoreQueryFilters()`
2. `includes` → `Include(...)`
3. `filter` → CNF `Where`
4. `search` + `searchFrom` → OR of `EF.Functions.Like`
5. `distinct` / `distinctOn` → dedupe
6. `groupBy` → collapse to first row per key
7. `orderBy` → sort (**after** distinct / group in the current implementation)

**Phase 2 — Count and page**

| Step | Behavior |
|------|----------|
| Count | Always runs `CountAsync` on the composed query → `totalCount` (or sole `count` when `count=true`). |
| Page | If not count-only: `AsSplitQuery()` when **3+** includes, then `Skip` / `Take` / `ToListAsync`. |
| Projection | `select` is applied **in memory** (`ProjectToDictionaries`), not as an EF `Select`. For SQL-side dictionary projection, see `DynamicSelectExtensions.SelectDynamic`. |

---

## Requirements

- **.NET 8** (sample target)
- **EF Core** against a relational provider (verify expression translation for your database)
- **Entity type** must be a **class** (`where TEntity : class`)
- This repository is a **sample web app** plus shared types; extract a **class library** if you want a clean NuGet package

---

## Backend (ASP.NET Core)

Bind the query string to **`CollectionQuery`** and pass it to **`CollectionQueryService`**:

```csharp
[HttpGet]
public async Task<IActionResult> GetItems([FromQuery] CollectionQuery query)
{
    var result = await _collectionQueryService.QueryAsync(_db.Items, query);
    return Ok(result);
}
```

ASP.NET Core understands **bracket notation** for lists and nested objects, for example `orderBy[0].field`, `filter[0][1].operator`, `searchFrom[0]`.

**Service setup:**

```csharp
var options = new CollectionQueryOptions
{
    UseProviderILike = true,
    // AllowedSelectFields = new(StringComparer.OrdinalIgnoreCase) { "Name", "Price", "Category.Name" },
};

var service = new CollectionQueryService(options);
var result = await service.QueryAsync(dbContext.Items, query, cancellationToken);
```

`QueryAsync` returns **`Task<object>`** — either `PagedResult<IDictionary<string, object?>>` or an anonymous `{ Count = int }`. See [API responses](#api-responses).

The sample creates **`new CollectionQueryService(...)`** in the controller; in production prefer **DI** (`AddScoped` / `AddSingleton`) and inject the service.

### `CollectionQueryOptions`

| Option | What to know |
|--------|----------------|
| `AllowedSelectFields` | **Enforced.** Throws if the client asks for a `select` path not in the set. |
| `UseProviderILike` | Passed through; **current** filter/search code still uses **`EF.Functions.Like`** for LIKE/ILIKE unless you extend `FilterHelper`. |
| `UseUnaccent` | Present on options but **not read** by this codebase yet. |
| `AllowedOrderFields`, `AllowedFilterFields`, `AllowedIncludePaths` | Declared for future tightening; validation in the service is **commented out**—add your own checks for public endpoints. |

### JSON

For entity graphs with cycles, use **`ReferenceHandler.IgnoreCycles`** (see `Program.cs`). Dictionary rows from `QueryAsync` are usually fine without extra configuration.

---

## Frontend (query strings)

Use **`GET`** with a query string. Keys use **indexed brackets** so ASP.NET can bind to `List<T>` and nested objects.

- **Illustrative path in examples:** `/api/items`
- **This repository’s demo:** `/api/posts`

### Full example (one line)

```http
GET /api/items?top=10&skip=0&search=test&searchFrom[0]=name&searchFrom[1]=description&orderBy[0].field=name&orderBy[0].direction=ASC&orderBy[0].nulls=NULLS%20LAST&orderBy[1].field=price&orderBy[1].direction=DESC&orderBy[1].nulls=NULLS%20LAST&filter[0][0].field=name&filter[0][0].operator=LIKE&filter[0][0].value=phone&filter[0][1].field=category&filter[0][1].operator=%3D&filter[0][1].value=electronics&filter[1][0].field=price&filter[1][0].operator=%3E&filter[1][0].value=100&includes[0]=category&select[0]=name&select[1]=price&groupBy[0]=category&count=true&withArchived=false
```

### Same example, parameter by parameter

| Parameter | Value |
|-----------|--------|
| `top` | `10` |
| `skip` | `0` |
| `search` | `test` |
| `searchFrom[0]` | `name` |
| `searchFrom[1]` | `description` |
| `orderBy[0].field` | `name` |
| `orderBy[0].direction` | `ASC` |
| `orderBy[0].nulls` | `NULLS LAST` (in URLs: `NULLS%20LAST`) |
| `orderBy[1].field` | `price` |
| `orderBy[1].direction` | `DESC` |
| `orderBy[1].nulls` | `NULLS LAST` |
| `filter[0][0].field` | `name` |
| `filter[0][0].operator` | `LIKE` |
| `filter[0][0].value` | `phone` |
| `filter[0][1].field` | `category` |
| `filter[0][1].operator` | `=` (often encoded as `%3D`) |
| `filter[0][1].value` | `electronics` |
| `filter[1][0].field` | `price` |
| `filter[1][0].operator` | `>` (often encoded as `%3E`) |
| `filter[1][0].value` | `100` |
| `includes[0]` | `category` |
| `select[0]` | `name` |
| `select[1]` | `price` |
| `groupBy[0]` | `category` |
| `count` | `true` |
| `withArchived` | `false` |

### Characters to URL-encode

| You mean | In the URL |
|----------|------------|
| Space (e.g. in `NULLS LAST`) | `%20` |
| `=` as a filter operator | `%3D` |
| `>` as a filter operator | `%3E` |

Equality should be the single character **`=`** in `operator`, not `==`.

### TypeScript example

```typescript
const q = new URLSearchParams();
q.set("top", "10");
q.set("skip", "0");
q.set("search", "test");
q.append("searchFrom[0]", "name");
q.append("searchFrom[1]", "description");
q.append("orderBy[0].field", "name");
q.append("orderBy[0].direction", "ASC");
q.append("filter[0][0].field", "name");
q.append("filter[0][0].operator", "LIKE");
q.append("filter[0][0].value", "phone");
q.append("filter[0][1].field", "category");
q.append("filter[0][1].operator", "=");
q.append("filter[0][1].value", "electronics");
q.append("includes[0]", "category");
q.append("select[0]", "name");
q.append("select[1]", "price");
q.append("filter[1][0].field", "price");
q.append("filter[1][0].operator", ">");
q.append("filter[1][0].value", "100");
q.set("count", "true");
q.set("withArchived", "false");

await fetch(`/api/items?${q.toString()}`);
```

If binding looks wrong for your ASP.NET version, compare the generated string with **Swagger** “Try it out” or a quick integration test.

---

## Query cheat sheet

CLR model property on `CollectionQuery` → typical query keys.

### Paging and flags

| Property | Query pattern | Type | Notes |
|----------|---------------|------|--------|
| `Top` | `top` | `int?` | Default **10**; forced to at least **1**. |
| `Skip` | `skip` | `int?` | Offset. |
| `Count` | `count` | `bool?` | `true` → only `{ count }`. |
| `WithArchived` | `withArchived` | `bool` | `true` → `IgnoreQueryFilters()`. |

### Search

| Property | Query pattern | Type | Notes |
|----------|---------------|------|--------|
| `Search` | `search` | `string?` | Substring search term. |
| `SearchFrom` | `searchFrom[i]` | `string` | OR together with `LIKE`. |

### Sort

| Property | Query pattern | Type | Notes |
|----------|---------------|------|--------|
| `OrderBy` | `orderBy[i].field` | `string` | Dot paths allowed. |
| | `orderBy[i].direction` | `ASC` / `DESC` | Enum name. |
| | `orderBy[i].nulls` | `string?` | Stored on model; **not applied** by current ordering code. |

### Filters (CNF)

| Property | Query pattern | Type | Notes |
|----------|---------------|------|--------|
| `Filter` | `filter[g][p].field` | `string` | Group `g`, predicate `p`. |
| | `filter[g][p].operator` | `string` | See [Filtering](#filtering). |
| | `filter[g][p].value` | `string?` | RHS; commas for `IN` / `BETWEEN`. |

### Includes and shape

| Property | Query pattern | Type | Notes |
|----------|---------------|------|--------|
| `Includes` | `includes[i]` | `string` | EF include path; first char uppercased. |
| `Select` | `select[i]` | `string` | Keys in each dictionary row. |
| `GroupBy` | `groupBy[i]` | `string` | First row per group. |
| `Distinct` | `distinct` | `bool?` | |
| `DistinctOn` | `distinctOn[i]` | `string` | With `distinct`. |

### Reserved

| Property | Query pattern | Notes |
|----------|---------------|--------|
| `Locale` | `locale` | Reserved. |
| `Cache` | `cache` | Reserved. |

---

## Filtering

### CNF (how `filter[g][p]` combines)

**CNF** means: inside one group index `g`, every predicate `filter[g][0]`, `filter[g][1]`, … is combined with **OR**. Different groups are combined with **AND**.

Example: `filter[0][0]`, `filter[0][1]`, `filter[1][0]` means:

`(predicate at [0][0] OR predicate at [0][1]) AND (predicate at [1][0])`

### Operators (`ApplyFilters` today)

| `operator` | `value` | Meaning |
|------------|---------|---------|
| `=`, `!=`, `<`, `<=`, `>`, `>=` | One scalar | Compared to the field (coerced to member type). |
| `IN` | Comma-separated list | Membership. |
| `BETWEEN` | Two comma-separated bounds | Inclusive range. |
| `LIKE`, `ILIKE` | Fragment | Wrapped as `%value%`, uses `EF.Functions.Like`. |
| `IS NULL`, `IS NOT NULL` | (omit) | Use exact operator strings; in URLs encode spaces (`IS%20NULL`). |

### Do not use (bug in current switch)

| `operator` | Problem |
|------------|---------|
| `NOT IN` | Routed like `IN` — wrong semantics. |
| `NOT BETWEEN` | Routed like `BETWEEN` — wrong semantics. |

Other strings exist on `FilterOperators` for documentation or future work; only the **working** table above is safe unless you extend `FilterHelper`.

---

## API responses

Default ASP.NET JSON uses **camelCase** property names on the wire.

**Paged response** (`count` is not `true`):

| JSON property | Meaning |
|---------------|---------|
| `items` | Array of objects (one dictionary per row). |
| `totalCount` | Rows matching the filter **before** paging. |
| `pageNumber` | Derived from `skip` and `top`. |
| `pageSize` | Effective `top`. |

**Count-only** (`count=true`):

| JSON property | Meaning |
|---------------|---------|
| `count` | Total rows matching the query. |

Examples:

```json
{
  "items": [{ "name": "Example", "price": 199.99, "category.name": "Electronics" }],
  "totalCount": 42,
  "pageNumber": 1,
  "pageSize": 10
}
```

```json
{ "count": 42 }
```

---

## Limitations

| Area | Detail |
|------|--------|
| Multi-field `distinctOn` / `groupBy` | May pull data client-side for part of the pipeline. Prefer **one field** if you need pure SQL. |
| `distinct` without `distinctOn` | Uses `Distinct()` on the entity; behavior depends on EF and your model—validate before exposing. |
| `orderBy[i].nulls` | Not wired into `ApplyOrdering` yet. |
| Search | Always wildcard `LIKE`; case rules follow your database collation. |
| Includes | Paths that walk back to the aggregate root are truncated for EF; projection may still expose scalars. |
| Security | Treat `filter`, `select`, `includes`, `orderBy` as **untrusted** unless you validate or allow-list. |

---

## Source layout

| File / folder | Role |
|---------------|------|
| `QuellectionQuery.cs` | `CollectionQuery`, `Order`, `FilterItem`, `PagedResult`, options, `PathExpr`, `FilterOperators` |
| `CollectionQueryService.cs` | End-to-end query execution and dictionary projection |
| `Extensions/FilterHelper.cs` | Filters, search, ordering, group-by, `ApplyDistinctOn` |
| `Extensions/DistinctHelper.cs` | Distinct-on helper using `PathExpr` (for custom pipelines) |
| `Extensions/DynamicSelectExtensions.cs` | `SelectDynamic` for EF-translatable dictionary `Select` (not used by the service today) |
| `Extensions/QueryableExtensions.cs` | Small `IQueryable` helpers |
| `Controllers/` | Demo `GET /api/posts` |

---

## License

Add a **`LICENSE`** file when you publish a package. Until then, usage follows your own policies for this repository.
