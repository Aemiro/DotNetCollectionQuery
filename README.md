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
- [Running the sample (testing)](#running-the-sample-testing)
- [License](#license)

---

## Overview

| Question | Answer |
|----------|--------|
| **What it does** | Binds a `CollectionQuery` model (usually from `[FromQuery]`) and runs it through `CollectionQueryService.QueryAsync<TEntity>()`. |
| **Stack** | .NET 8+, ASP.NET Core, EF Core (sample uses EF Core 9 + Npgsql). |
| **Typical output** | Either a paged list of dictionary rows, or `{ "count": N }` when `count=true`. |
| **Try it in this repo** | See [Running the sample (testing)](#running-the-sample-testing): `GET /api/posts`, Swagger, and example URLs. |

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

This repo’s reference implementation is **`Controllers/PostsController.cs`**: `GET /api/posts` binds **`CollectionQuery`** from the query string and runs **`QueryAsync`** on **`DbSet<Post>`**.

```40:46:Controllers/PostsController.cs
        [HttpGet]
        public async Task<ActionResult<PagedResult<PostDto>>> GetAll([FromQuery()] CollectionQuery query)
        {
            query.OrderBy = query.OrderBy ?? [new Order { Field = "CreatedAt", Direction = Direction.DESC }];
            var result = await _collectionQueryService.QueryAsync(_db.Posts, query);

            return Ok(result);
```

The controller constructs **`CollectionQueryService`** with options aligned to that file (`UseProviderILike`, `UseUnaccent`, and commented allow-lists you can enable). ASP.NET Core binds bracket keys such as `orderBy[0].field`, `filter[0][1].operator`, and `searchFrom[0]`.

`QueryAsync` returns **`Task<object>`** — either `PagedResult<IDictionary<string, object?>>` or `{ Count = int }` when `count=true`. See [API responses](#api-responses).

For production, prefer **DI** (`AddScoped` / `AddSingleton`) instead of `new CollectionQueryService(...)` inside the controller.

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

All examples below target **`GET /api/posts`** from [`Controllers/PostsController.cs`](Controllers/PostsController.cs). Base URL when running locally: **`http://localhost:5132`** (see `Properties/launchSettings.json`).

Use **`GET`** with **indexed bracket** keys so ASP.NET Core binds to `CollectionQuery` lists and nested objects.

### Fields you can use on `Post` (sample API)

Paths are **case-insensitive**. Dot notation reaches **`Category`** (include it when filtering or selecting nested category fields).

| Kind | Paths (examples) |
|------|-------------------|
| **Scalars on `Post`** | `Id`, `Title`, `Slug`, `Content`, `CategoryId`, `PublishedAt`, `CreatedAt`, `UpdatedAt` |
| **Via `Category`** | `Category.Name`, `Category.Slug`, `Category.Description` |
| **Includes** | `Category`, `Comments` (string must match EF navigation names; service uppercases the first character of the path) |

Seeded data (after first run) includes posts such as **“Introducing CollectionQuery”** and **“A Day in Life”**, and categories **Technology** / **Lifestyle** — use those strings in `search` / `filter` to see matches.

### Full example (one line, `PostsController`)

Combines paging, search on title and content, explicit sort, filter on category name, include, select, and `groupBy` (works with seeded rows):

```http
GET http://localhost:5132/api/posts?top=5&skip=0&search=Collection&searchFrom[0]=Title&searchFrom[1]=Content&orderBy[0].field=Title&orderBy[0].direction=ASC&filter[0][0].field=Category.Name&filter[0][0].operator=LIKE&filter[0][0].value=Tech&includes[0]=Category&select[0]=Title&select[1]=Slug&select[2]=Category.Name&groupBy[0]=CategoryId
```

If you **omit `orderBy`**, the controller applies **`CreatedAt` DESC** before calling the service (see snippet under [Backend](#backend-aspnet-core)).

### Same example, parameter by parameter

| Query parameter | Value |
|-----------------|--------|
| `top` | `5` |
| `skip` | `0` |
| `search` | `Collection` |
| `searchFrom[0]` | `Title` |
| `searchFrom[1]` | `Content` |
| `orderBy[0].field` | `Title` |
| `orderBy[0].direction` | `ASC` |
| `filter[0][0].field` | `Category.Name` |
| `filter[0][0].operator` | `LIKE` |
| `filter[0][0].value` | `Tech` |
| `includes[0]` | `Category` |
| `select[0]` | `Title` |
| `select[1]` | `Slug` |
| `select[2]` | `Category.Name` |
| `groupBy[0]` | `CategoryId` |

### Characters to URL-encode

| You mean | In the URL |
|----------|------------|
| Space (e.g. in `NULLS LAST`) | `%20` |
| `=` as a filter operator | `%3D` |
| `>` as a filter operator | `%3E` |

Equality should be the single character **`=`** in `operator`, not `==`.

### TypeScript example (`GET /api/posts`)

```typescript
const base = "http://localhost:5132/api/posts";
const q = new URLSearchParams();
q.set("top", "5");
q.set("skip", "0");
q.set("search", "Collection");
q.append("searchFrom[0]", "Title");
q.append("searchFrom[1]", "Content");
q.append("orderBy[0].field", "Title");
q.append("orderBy[0].direction", "ASC");
q.append("filter[0][0].field", "Category.Name");
q.append("filter[0][0].operator", "LIKE");
q.append("filter[0][0].value", "Tech");
q.append("includes[0]", "Category");
q.append("select[0]", "Title");
q.append("select[1]", "Slug");
q.append("select[2]", "Category.Name");
q.append("groupBy[0]", "CategoryId");

const res = await fetch(`${base}?${q.toString()}`);
const data = await res.json();
```

Compare the built URL with **Swagger** `GET /api/posts` if any parameter fails to bind.

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
  "items": [
    {
      "title": "Introducing CollectionQuery",
      "slug": "introducing-collectionquery",
      "category.name": "Technology"
    }
  ],
  "totalCount": 2,
  "pageNumber": 1,
  "pageSize": 5
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
| `Controllers/PostsController.cs` | Demo list: `GET /api/posts` + `[FromQuery] CollectionQuery`; other verbs for CRUD |

---

## Running the sample (testing)

This repository is a runnable **ASP.NET Core** app. Startup runs **EF Core migrations** and **seeds** sample categories, posts, and comments if the database is empty.

### Prerequisites

| Requirement | Notes |
|---------------|--------|
| **.NET 8 SDK** | `dotnet --version` should report 8.x (project targets `net8.0`). |
| **PostgreSQL** | Running locally or reachable from your machine. |
| **Database** | Default connection string uses database name **`blogs`** on `localhost` (see `appsettings.json`). The app calls `MigrateAsync` on startup—it will create the database and schema if needed. |

### 1. Configure PostgreSQL

Edit **`appsettings.json`** → `ConnectionStrings:DefaultConnection`, or override with user secrets (recommended for passwords):

```powershell
cd path\to\DotNetCollectionQuery
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=blogs;Username=postgres;Password=YOUR_PASSWORD;"
```

Ensure the PostgreSQL user can create databases (or create an empty database named `blogs` first—migrations will still apply).

### 2. Run the API

From the project directory (where `CollectionQuery.csproj` is):

```powershell
dotnet run
```

Default HTTP URL (from `Properties/launchSettings.json`): **`http://localhost:5132`**. HTTPS profile also uses **`https://localhost:7100`**.

If PostgreSQL is down or the connection string is wrong, startup fails when the **data seeder** runs (after `MigrateAsync`).

### 3. Open Swagger (easiest manual test)

With **`ASPNETCORE_ENVIRONMENT=Development`** (default for `dotnet run`):

| Step | Action |
|------|--------|
| 1 | Browse to **`http://localhost:5132/swagger`**. |
| 2 | Expand **`GET /api/posts`**. |
| 3 | Fill query fields (or paste a query string) and execute. |

Swagger is only registered in **Development** (see `Program.cs`).

### 4. Try `GET /api/posts` from a browser or curl

Same behavior as **`GetAll`** in [`Controllers/PostsController.cs`](Controllers/PostsController.cs): **`QueryAsync(_db.Posts, query)`** and default **`OrderBy`: `CreatedAt` DESC** when the client sends no `orderBy`.

| Goal | Request |
|------|---------|
| Minimal list (default sort) | `GET http://localhost:5132/api/posts?top=10` |
| Search title | `GET http://localhost:5132/api/posts?search=Collection&searchFrom[0]=Title&top=10` |
| Include category + select fields | `GET http://localhost:5132/api/posts?includes[0]=Category&select[0]=Title&select[1]=Category.Name&top=5` |
| Count only | `GET http://localhost:5132/api/posts?count=true` |
| Filter category name (needs include) | `GET http://localhost:5132/api/posts?filter[0][0].field=Category.Name&filter[0][0].operator=LIKE&filter[0][0].value=Tech&includes[0]=Category` |
| Full pipeline in one URL | See **Frontend** → *Full example (one line, PostsController)* in this README. |

Seeded **`Post`** rows use titles **“Introducing CollectionQuery”** and **“A Day in Life”**; categories include **Technology** and **Lifestyle** (`Services/DataSeeder.cs`).

### 5. Optional: migrations only (CLI)

Migrations already run on startup. To apply from the command line without running the web app:

```powershell
dotnet ef database update
```

Requires the EF CLI tools (`dotnet tool install --global dotnet-ef`) and a valid connection string.

### 6. Resetting test data

To re-run the **seed** logic from scratch, drop the **`blogs`** database (or delete all rows in `Categories` / `Posts` / `Comments` so the seeder’s “any categories?” check allows a fresh seed—see `Services/DataSeeder.cs`), then start the app again.

---

## License

Add a **`LICENSE`** file when you publish a package. Until then, usage follows your own policies for this repository.
