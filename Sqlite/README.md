# SQLite repository library

`Sqlite` provides the embedded relational counterpart to the sibling MongoDB and PostgreSQL repositories. It uses Entity Framework Core and Microsoft.Data.Sqlite to expose strongly typed CRUD, audit fields, soft deletion, projection, pagination, collection-value removal, index management, and table lifecycle helpers — against a zero-configuration, file- or memory-backed database.

For the side-by-side database comparison and solution-wide guidance, see the [root documentation](../README.md).

## Technical specification

| Area | Specification |
|---|---|
| Target framework | .NET 10 (`net10.0`) |
| Package and assembly | `Sqlite` |
| ORM | Entity Framework Core 10.0.10 |
| Provider | Microsoft.EntityFrameworkCore.Sqlite 10.0.10 |
| Identifier | GUID represented as a string, maximum length 64 |
| Data access | EF Core LINQ expressions |
| Table naming | Type or `[TableName]` converted to `snake_case`; trailing `Entity` removed |
| Audit behavior | Automatic created, updated, deleted, and soft-delete fields |

## When to use it

Choose this library when the application needs an embedded, serverless relational store: desktop and mobile apps, CLI tools, local caches, offline-first workloads, integration tests, and small services with a single writer. PostgreSQL is the better fit for concurrent multi-writer server workloads; MongoDB fits document-shaped data with heavy nested-array operations.

### Advantages

- Zero infrastructure: no server, no container, no credentials — a single file (or in-memory database).
- Familiar EF Core and LINQ query model, identical workflow to the PostgreSQL repository.
- Consistent CRUD workflow with the MongoDB and PostgreSQL repositories.
- Built-in audit timestamps, soft delete, restore, projection, and pagination.
- Simple, unique, compound, and directional index helpers.
- Runtime table inspection, creation, and reset helpers for controlled scenarios.
- Excellent for tests and demos: the full repository behavior runs in-process.

### Tradeoffs

- Single-writer concurrency model; heavy concurrent writes serialize at the database level.
- No network access layer: all readers and writers need access to the database file.
- SQLite has no `TRUNCATE`; `TruncateTableAsync` issues an unfiltered `DELETE` (and resets `sqlite_sequence` when asked).
- The `cascade` truncate argument is accepted for API parity but ignored; cascades follow the schema's `ON DELETE` rules.
- Each `SqliteDbContext<T>` models one entity type, so this abstraction is not intended for relationship-rich aggregate graphs.
- Repository and `DbContext` instances are scoped units of work and are not safe for concurrent use.
- `PullAsync` loads matching entities, modifies their collection values in memory, and persists them.
- SQLite has no MongoDB-style TTL index in this API.
- `[SensitiveData]` is metadata only; it does not encrypt, redact, or omit a value automatically.

## Installation

Reference the project while developing in this monorepo:

```xml
<ProjectReference Include="..\path\to\Sqlite\Sqlite\Sqlite.csproj" />
```

When consuming a packed release, reference the package version produced by the repository:

```xml
<PackageReference Include="Sqlite" Version="VERSION" />
```

## Define an entity

Entities must inherit `SqliteEntity` or implement `ISqliteEntity`.

```csharp
using Sqlite;
using Sqlite.Helpers;

[TableName("ApplicationUsers")]
public sealed class UserEntity : SqliteEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];

    [SensitiveData]
    public string PasswordHash { get; set; } = string.Empty;
}
```

This example maps to `application_users`. `SqliteEntity` supplies `Id`, `CreatedDateTime`, `UpdatedDateTime`, `DeletedDateTime`, and `IsDeleted`.

## Create and register a repository

Create a repository directly:

```csharp
var repository = new SqliteRepository<UserEntity>("Data Source=application.db");
```

For dependency injection, register a scoped context and repository:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sqlite;

services.AddScoped(_ => new SqliteDbContext<UserEntity>(connectionString));
services.AddScoped<ISqliteRepository<UserEntity>, SqliteRepository<UserEntity>>();
```

Prefer the context-based constructor when the application controls resource lifetime: disposing the context releases the database file. `Data Source=:memory:` databases live only as long as their connection, so use a shared-cache named connection or supply an externally managed open connection for in-memory scenarios.

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
using Sqlite.Helpers;

var admins = await repository.GetAllAsync(
    predicate: user => user.Roles.Contains("Admin"),
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

SQLite projections are standard LINQ expressions and are translated by EF Core:

```csharp
var summaries = await repository.GetAllAsync(
    projection: user => new
    {
        user.Id,
        user.Name,
        user.Email
    },
    predicate: user => user.Roles.Contains("Admin"));
```

Use provider-translatable expressions and project only the columns the caller needs.

## Remove collection values

```csharp
await repository.PullAsync(
    field: user => user.Roles,
    fieldFilter: role => role == "Temporary",
    documentPredicate: user => user.Email.EndsWith("@example.test"));
```

Unlike MongoDB's atomic array operator, this implementation reads matching rows, updates the mapped collection in memory, and saves changes through EF Core. Keep the predicate selective for large tables.

## Table lifecycle

```csharp
bool exists = await repository.TableExistsAsync();
bool auditExists = await repository.TableExistsAsync("audit_events");

await repository.EnsureTableAsync();
await repository.TruncateTableAsync(restartIdentity: true);
```

`TableExistsAsync` queries `sqlite_master` and compares names case-insensitively; the optional `schema` argument exists for API parity with the relational siblings and is ignored. `EnsureTableAsync` is convenient for demonstrations, tests, and runtime-owned tables. `TruncateTableAsync` issues an unfiltered `DELETE` (SQLite has no `TRUNCATE`), optionally resets the table's `AUTOINCREMENT` counter in `sqlite_sequence`, bypasses soft deletion, and should be restricted to intentional administrative or test workflows.

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

The directional overload accepts `(property expression, SortDirection)` tuples. The `expiresAfter` overload creates a normal index because SQLite has no native TTL index. Automatic retention requires application cleanup or a scheduled job. The current `filter` argument is reserved and does not create a partial `WHERE` index.

## Soft-delete behavior

List, paging, first, projection, and existence operations exclude soft-deleted rows by default. Pass the applicable `includeDeletes` or `includeDeleted` argument when an API provides it and deleted rows must be returned. Restore clears the deletion state; hard delete permanently removes a row.

`CountAsync` and `GetSingleOrDefaultAsync` execute their supplied predicates, so include `!entity.IsDeleted` explicitly when counting or selecting only active records.

## Console demonstration

The sibling console app is a self-checking executable specification. Because SQLite is embedded, it needs no Docker, container, or server: it creates an ephemeral database file in the system temp directory and performs a complete deterministic workflow:

1. Resolve table-name and sensitive-data attributes without printing sensitive values.
2. Ensure `feature_examples`, verify mapped and named existence checks, and reset it with the `DELETE`-based truncate.
3. Create simple, TTL-overload, compound unique, and directional indexes.
4. Explicitly report that TTL produces a normal SQLite index and partial-filter translation is reserved.
5. Insert one row and bulk-insert five rows with audit timestamps.
6. Count, check existence, and run ID, first, single, filtered, sorted, and projected reads.
7. Run single-field and multi-field pagination with totals and navigation flags.
8. Update one row and apply a transaction-backed shared update.
9. Remove temporary tags with EF Core read/modify/write `PullAsync`.
10. Exercise ID, entity, predicate, and collection deletion workflows.
11. Verify default soft-delete filtering, include-deleted reads, both restore overloads, and hard deletion.
12. Remove every demonstration index.
13. Dispose the context and delete the database file.

The app contains no static connection string or database credential.

Prerequisites:

- .NET 10 SDK. Nothing else — no Docker engine is required.

Run it from the repository root:

```powershell
dotnet run --project Sqlite/Sqlite.Console/Sqlite.Console.csproj
```

The database file and its data are removed when the process exits.

Each successful check prints `[OK]`. Any unexpected result throws an exception and makes the process fail.

## Tests and coverage

```powershell
dotnet test Sqlite/Sqlite.Tests/Sqlite.Tests.csproj
dotnet test Sqlite/Sqlite.Tests/Sqlite.Tests.csproj --collect:"XPlat Code Coverage"
```

The SQLite suite currently contains 65 passing tests. The latest coverage collection records 100% of production lines and branches for the `Sqlite` assembly (566/566 lines and 198/198 branches).

Coverage is a safety signal rather than a substitute for meaningful assertions. Preserve behavior-focused tests when extending the API.

## Operational guidance

- Use one scoped repository/context per unit of work.
- Await each database operation before starting another on the same context.
- Prefer the context-based constructor so the application controls when the database file is released.
- Enable write-ahead logging (`PRAGMA journal_mode=WAL`) for concurrent-reader workloads.
- Index fields used by common predicates and sorts, but avoid redundant indexes.
- Prefer projections and pagination for large data sets.
- Use UTC timestamps consistently.
- Review hard deletes and truncation carefully before production use.

Copyright 2026 Devspace.
