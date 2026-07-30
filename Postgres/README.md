# PostgreSQL repository library

`Postgres` provides the relational counterpart to the sibling MongoDB repository. It uses Npgsql and ADO.NET commands to expose strongly typed CRUD, audit fields, soft deletion, projection, pagination, collection-value removal, index management, and table lifecycle helpers.

For the side-by-side MongoDB/PostgreSQL comparison and solution-wide guidance, see the [root documentation](../README.md).

## Technical specification

| Area | Specification |
|---|---|
| Target framework | .NET 10 (`net10.0`) |
| Package and assembly | `Postgres` |
| Data layer | ADO.NET; no ORM |
| Provider | Npgsql 10.0.3 |
| Identifier | GUID represented as a string, maximum length 64 |
| Data access | Parameterized SQL translated from a focused LINQ-expression subset |
| Table naming | Type or `[TableName]` converted to `snake_case`; trailing `Entity` removed |
| Audit behavior | Automatic created, updated, deleted, and soft-delete fields |

## When to use it

Choose this library when the application depends on relational constraints, SQL tooling, transactions, reporting, or a structured schema. MongoDB is often a better fit for document-shaped data that changes frequently or relies heavily on native nested-array operations.

### Advantages

- Low-overhead, parameterized Npgsql commands with a familiar expression-based API.
- PostgreSQL constraints, transactions, indexing, and operational ecosystem.
- Consistent CRUD workflow with the MongoDB repository.
- Built-in audit timestamps, soft delete, restore, projection, and pagination.
- Simple, unique, compound, and directional index helpers.
- Runtime table inspection, creation, and truncation helpers for controlled scenarios.

### Tradeoffs

- Schema evolution needs disciplined migrations in production.
- Each repository maps one entity type, so this abstraction is not intended for relationship-rich aggregate graphs.
- A repository owns one connection and is not intended for concurrent operations.
- `PullAsync` loads matching entities, modifies their collection values in memory, and persists them; this can cost more than MongoDB's server-side array update.
- PostgreSQL has no native MongoDB-style TTL index in this API.
- `[SensitiveData]` is metadata only; it does not encrypt, redact, or omit a value automatically.

## Installation

Reference the project while developing in this monorepo:

```xml
<ProjectReference Include="..\path\to\Postgres\Postgres\Postgres.csproj" />
```

When consuming a packed release, reference the package version produced by the repository:

```xml
<PackageReference Include="Postgres" Version="VERSION" />
```

## Define an entity

Entities must inherit `PostgresEntity` or implement `IPostgresEntity`.

```csharp
using Postgres;
using Postgres.Helpers;

[TableName("ApplicationUsers")]
public sealed class UserEntity : PostgresEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];

    [SensitiveData]
    public string PasswordHash { get; set; } = string.Empty;
}
```

This example maps to `application_users`. `PostgresEntity` supplies `Id`, `CreatedDateTime`, `UpdatedDateTime`, `DeletedDateTime`, and `IsDeleted`.

## Create and register a repository

Create a repository directly:

```csharp
var repository = new PostgresRepository<UserEntity>(connectionString);
```

For dependency injection, register a scoped repository:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Postgres;

services.AddScoped<IPostgresRepository<UserEntity>>(_ =>
    new PostgresRepository<UserEntity>(connectionString));
```

Keep credentials outside source control. Use a secret store, environment variable, managed identity, or another deployment-specific configuration provider.

## CRUD operations

```csharp
var user = new UserEntity
{
    Name = "Ada Lovelace",
    Email = "ada@example.test",
    Roles = ["Admin", "Temporary"]
};

await repository.InsertAsync(user);

UserEntity? stored = await repository.GetByIdAsync(user.Id);

stored!.Name = "Augusta Ada King";
await repository.UpdateAsync(stored);

await repository.DeleteAsync(stored);                  // soft delete
await repository.RestoreAsync(stored);                 // restore
await repository.DeleteAsync(stored, hardDelete: true); // physical delete
```

Bulk insert and shared-field updates are also supported:

```csharp
await repository.InsertAsync(users);

await repository.UpdateManyAsync(users, new Dictionary<string, object>
{
    [nameof(UserEntity.Email)] = "archived@example.test"
});
```

The update dictionary uses CLR property names. Invalid, unmapped, or incompatible values fail at runtime, so prefer `nameof` over string literals.

## Query, sort, and page

```csharp
using Postgres.Helpers;

var admins = await repository.GetAllAsync(
    predicate: user => user.Email.EndsWith("@example.test"),
    orderBy: user => user.Name,
    sortDirection: SortDirection.Ascending);

var page = await repository.GetPagedListAsync(
    pageIndex: 1,
    pageSize: 25,
    predicate: user => user.Email.EndsWith("@example.test"),
    orderBy: user => user.Name,
    sortDirection: SortDirection.Ascending);

Console.WriteLine($"{page.Items.Count} of {page.TotalCount}");
Console.WriteLine($"Page {page.PageIndex} of {page.TotalPages}");
```

Use object initializers for multi-column sorting:

```csharp
var ordering = new[]
{
    new SortExpression<UserEntity>
    {
        Expression = user => user.Name,
        SortDirection = SortDirection.Ascending
    },
    new SortExpression<UserEntity>
    {
        Expression = user => user.CreatedDateTime,
        SortDirection = SortDirection.Descending
    }
};

var page = await repository.GetPagedListAsync(1, 25, null, ordering);
```

Page indexes are one-based. `pageIndex` and `pageSize` must be positive.

## Projection

PostgreSQL projections are standard LINQ expressions applied after matching rows are materialized:

```csharp
var summaries = await repository.GetAllAsync(
    projection: user => new
    {
        user.Id,
        user.Name,
        user.Email
    },
    predicate: user => user.Email.EndsWith("@example.test"));
```

Predicates are translated to parameterized SQL. Entity property comparisons, Boolean composition, null checks, string `StartsWith`/`EndsWith`/`Contains`, and constant-collection `Contains` are supported. Unsupported predicates throw `NotSupportedException`.

## Remove collection values

```csharp
await repository.PullAsync(
    field: user => user.Roles,
    fieldFilter: role => role == "Temporary",
    documentPredicate: user => user.Email.EndsWith("@example.test"));
```

Unlike MongoDB's atomic array operator, this implementation reads matching rows, updates the mapped collection in memory, and writes changes with parameterized commands. Keep the predicate selective for large tables.

## Table lifecycle

```csharp
bool exists = await repository.TableExistsAsync();
bool auditExists = await repository.TableExistsAsync("audit_events", "public");

await repository.EnsureTableAsync();
await repository.TruncateTableAsync(restartIdentity: true, cascade: false);
```

`EnsureTableAsync` is convenient for demonstrations and isolated runtime-owned tables. Prefer reviewed, versioned SQL scripts for production schema evolution. `TruncateTableAsync` bypasses soft deletion and should be restricted to intentional administrative or test workflows.

## Indexes

```csharp
await repository.CreateIndexAsync(user => user.Email);

await repository.CreateIndexAsync(
    fields: new Expression<Func<UserEntity, object>>[]
    {
        user => user.Email,
        user => user.Name
    },
    unique: true);

await repository.RemoveIndexAsync(user => user.Email);
```

The directional overload accepts `(property expression, SortDirection)` tuples. The `expiresAfter` overload creates a normal index because PostgreSQL has no native TTL index. Automatic retention requires a scheduled database job or application cleanup. The current `filter` argument is reserved and does not create a partial `WHERE` index.

## Soft-delete behavior

List, paging, first, projection, and existence operations exclude soft-deleted rows by default. Pass the applicable `includeDeletes` or `includeDeleted` argument when an API provides it and deleted rows must be returned. Restore clears the deletion state; hard delete permanently removes a row.

`CountAsync` and `GetSingleOrDefaultAsync` execute their supplied predicates without the default soft-delete filter, so include `entity.DeletedDateTime == null` when selecting only active records.

## Console demonstration

The sibling console app is a self-checking executable specification. It starts `postgres:15.1` with Testcontainers and performs a complete deterministic workflow:

1. Resolve table-name and sensitive-data attributes without printing sensitive values.
2. Ensure `feature_examples`, verify mapped/named/schema-qualified existence, and truncate it.
3. Create simple, TTL-overload, compound unique, and directional indexes.
4. Explicitly report that TTL produces a normal PostgreSQL index and partial-filter translation is reserved.
5. Insert one row and bulk-insert five rows with audit timestamps.
6. Count, check existence, and run ID, first, single, filtered, sorted, and projected reads.
7. Run single-field and multi-field pagination with totals and navigation flags.
8. Update one row and apply a transaction-backed shared update.
9. Remove temporary tags with ADO.NET read/modify/write `PullAsync`.
10. Exercise ID, entity, predicate, and collection deletion workflows.
11. Verify default soft-delete filtering, include-deleted reads, both restore overloads, and hard deletion.
12. Remove every demonstration index.

The app contains no static connection string or database credential.

Prerequisites:

- .NET 10 SDK.
- Docker Desktop or another Docker-compatible engine running locally.
- Permission to pull the image on the first run.

Run it from the repository root:

```powershell
dotnet run --project Postgres/Postgres.Console/Postgres.Console.csproj
```

The container and its data are disposed when the process exits.

Each successful check prints `[OK]`. Any unexpected result throws an exception and makes the process fail.

## Tests and coverage

```powershell
dotnet test Postgres/Postgres.Tests/Postgres.Tests.csproj
dotnet test Postgres/Postgres.Tests/Postgres.Tests.csproj --collect:"XPlat Code Coverage"
```

The PostgreSQL suite includes ADO.NET repository behavior tests plus entity, expression-helper, paging, attribute, and sorting tests.

Coverage is a safety signal rather than a substitute for meaningful assertions. Preserve behavior-focused tests when extending the API.

## Operational guidance

- Use one scoped repository per unit of work.
- Await each database operation before starting another on the same repository.
- Use versioned SQL migrations for production schema changes.
- Index fields used by common predicates and sorts, but avoid redundant indexes.
- Prefer projections and pagination for large data sets.
- Use UTC timestamps consistently.
- Review hard deletes, truncation, and cascade behavior carefully before production use.

Copyright 2026 Devspace.
