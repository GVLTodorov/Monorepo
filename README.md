# Devspace data repositories

Production-oriented repository abstractions for MongoDB, PostgreSQL, and SQLite on .NET 10.

This solution provides three strongly typed libraries with intentionally similar workflows for CRUD, bulk operations, audit timestamps, soft deletion, restore, projections, sorting, pagination, collection updates, and index management. Provider-specific behavior remains available where the databases differ.

## NuGet packages

- [Devspace.Repositories.MongoDB](https://www.nuget.org/packages/Devspace.Repositories.MongoDB)
- [Devspace.Repositories.PostgreSQL](https://www.nuget.org/packages/Devspace.Repositories.PostgreSQL)
- [Devspace.Repositories.SQLite](https://www.nuget.org/packages/Devspace.Repositories.SQLite)

## Contents

- [Projects](#projects)
- [Choose a database](#choose-a-database)
- [Feature comparison](#feature-comparison)
- [Mongo library](#mongo-library)
- [Postgres library](#postgres-library)
- [Sqlite library](#sqlite-library)
- [Shared behavior](#shared-behavior)
- [Console applications](#console-applications)
- [Benchmarks](#benchmarks)
- [Testing and coverage](#testing-and-coverage)
- [Technical specifications](#technical-specifications)
- [Production guidance](#production-guidance)
- [Known limitations](#known-limitations)
- [Build commands](#build-commands)

## Projects

```text
Monorepo/
|-- Mongo/
|   |-- Mongo/                  MongoDB repository library
|   |-- Mongo.Benchmark/        BenchmarkDotNet + MongoDB Testcontainers
|   |-- Mongo.Console/          MongoDB Testcontainers demonstration
|   `-- Mongo.Tests/            NUnit unit tests
|-- Postgres/
|   |-- Postgres/               PostgreSQL repository library
|   |-- Postgres.Benchmark/     BenchmarkDotNet + PostgreSQL Testcontainers
|   |-- Postgres.Console/       PostgreSQL Testcontainers CRUD demonstration
|   `-- Postgres.Tests/         NUnit unit and provider-level tests
|-- Sqlite/
|   |-- Sqlite/                 SQLite repository library
|   |-- Sqlite.Benchmark/       BenchmarkDotNet + ephemeral SQLite database
|   |-- Sqlite.Console/         SQLite embedded CRUD demonstration (no Docker)
|   `-- Sqlite.Tests/           NUnit unit and provider-level tests
|-- Monorepo.sln
`-- README.md
```

All twelve projects target `net10.0`. The three library projects are packable and generate XML API documentation.

## Choose a database

| Choose MongoDB when | Choose PostgreSQL when | Choose SQLite when |
|---|---|---|
| Documents have flexible or evolving shapes | The domain benefits from a strongly defined relational schema | The application needs an embedded, zero-configuration store |
| Nested objects and arrays are central to the model | Foreign keys, constraints, and relational integrity are important | Desktop, mobile, CLI, or offline-first workloads dominate |
| Native document TTL indexes are required | Multi-row ACID transactions are a primary requirement | A single writer with in-process access is sufficient |
| The workload naturally maps to document access patterns | SQL reporting, joins, and established relational tooling matter | Local caches, prototypes, and tests should run without a server |
| Horizontal document distribution is expected | Complex relational querying and ADO.NET control are preferred | Deployment must avoid external database infrastructure |

The repository APIs reduce application-level differences, but they do not make the databases interchangeable. Schema design, indexing, transaction behavior, query translation, and deployment topology must still be designed for the selected provider.

## Feature comparison

| Capability | Mongo | Postgres | Sqlite |
|---|---:|---:|---:|
| Strongly typed predicates | Yes | Yes, translated to parameterized SQL | Yes, translated to parameterized SQL |
| Single and bulk insert | Yes | Yes | Yes |
| Single and bulk update | Yes | Yes | Yes |
| Soft and hard delete | Yes | Yes | Yes |
| Restore soft-deleted records | Yes | Yes | Yes |
| Projection | MongoDB `ProjectionDefinition` | LINQ expression | LINQ expression |
| One- and multi-column sorting | Yes | Yes | Yes |
| Pagination metadata | Yes | Yes | Yes |
| Collection item removal | Native MongoDB pull operation | ADO.NET read/modify/write | ADO.NET read/modify/write |
| Simple, compound, directional, and unique indexes | Yes | Yes | Yes |
| Partial indexes | Yes | Not implemented; the current filter argument is reserved | Not implemented; the current filter argument is reserved |
| TTL indexes | Native | Not native; the TTL overload creates a normal index | Not native; the TTL overload creates a normal index |
| Automatic storage creation | MongoDB creates collections on first write | Repository can ensure the mapped table exists | Repository can ensure the mapped table exists |
| Table truncation | Not applicable | Yes, with identity restart and cascade options | `DELETE`-based reset with `sqlite_sequence` restart; cascade not applicable |
| Provider-level transactions for bulk mutation | Requires a transaction-capable MongoDB deployment | ADO.NET transaction | ADO.NET transaction |
| External infrastructure | MongoDB deployment | PostgreSQL server | None; embedded file or in-memory database |

## Mongo library

### Specification

| Property | Value |
|---|---|
| Project | `Mongo/Mongo/Mongo.csproj` |
| Assembly and package ID | `Mongo` |
| Target framework | `.NET 10` |
| Driver | `MongoDB.Driver 3.10.0` |
| BSON support | `MongoDB.Bson 3.10.0` |
| Entity contract | `IMongoEntity` |
| Base entity | `MongoEntity` |
| Repository contract | `IMongoRepository<T>` |
| Identifier | MongoDB ObjectId represented as a string |
| Default storage name | Lowercase type name with `entity` removed |

### Reference the project

Use a project reference while consuming the library from this monorepo:

```xml
<ItemGroup>
  <ProjectReference Include="..\Mongo\Mongo\Mongo.csproj" />
</ItemGroup>
```

### Define an entity

```csharp
using Mongo.Helpers;

[CollectionName("users")]
public sealed class UserEntity : MongoEntity
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public DateTime LastActiveUtc { get; set; }
}
```

`MongoEntity` supplies `Id`, `CreatedDateTime`, `UpdatedDateTime`, `DeletedDateTime`, and the computed `IsDeleted` property. Implement `IMongoEntity` directly when inheritance is not appropriate.

### Create a repository

```csharp
using Mongo;
using MongoDB.Driver;

var connectionString = configuration.GetConnectionString("Mongo")
    ?? throw new InvalidOperationException("Mongo connection string is missing.");

IMongoClient client = new MongoClient(connectionString);
IMongoRepository<UserEntity> users = new MongoRepository<UserEntity>(
    client,
    database: "application");
```

Keep credentials outside source control. Environment variables, a secret manager, or the deployment platform's secret store should provide the connection string.

### CRUD and query example

```csharp
var user = new UserEntity
{
    Name = "Ada Lovelace",
    Email = "ada@example.com",
    Roles = ["Administrator"],
    LastActiveUtc = DateTime.UtcNow
};

// Create
await users.InsertAsync(user);

// Read
var stored = await users.GetByIdAsync(user.Id);
var exists = await users.ExistsAsync(x => x.Email == "ada@example.com");

// Update
stored!.Name = "Augusta Ada King";
await users.UpdateAsync(stored);

// Soft delete and restore
await users.DeleteAsync(stored);
await users.RestoreAsync(stored.Id);

// Permanent deletion
await users.DeleteAsync(stored, hardDelete: true);
```

### Filtering, sorting, and pagination

```csharp
using Mongo.Helpers;

var page = await users.GetPagedListAsync(
    pageIndex: 1,
    pageSize: 25,
    predicate: x => x.Roles.Contains("Administrator"),
    orderBy: x => x.LastActiveUtc,
    sortDirection: SortDirection.Descending,
    includeDeletes: false);

Console.WriteLine($"Page {page.PageIndex} of {page.TotalPages}");
Console.WriteLine($"Matching users: {page.TotalCount}");
```

For deterministic multi-field ordering:

```csharp
var order = new[]
{
    new SortExpression<UserEntity>
    {
        Expression = x => x.Name,
        SortDirection = SortDirection.Ascending
    },
    new SortExpression<UserEntity>
    {
        Expression = x => x.CreatedDateTime,
        SortDirection = SortDirection.Descending
    }
};

var page = await users.GetPagedListAsync(1, 25, null, order);
```

### Projection

```csharp
using MongoDB.Driver;

var projection = Builders<UserEntity>.Projection.Expression(x => new
{
    x.Id,
    x.Name,
    x.Email
});

var summaries = await users.GetAllAsync(
    projection,
    predicate: x => x.Roles.Contains("Administrator"));
```

### Bulk and collection operations

```csharp
var selectedUsers = await users.GetAllAsync(x => x.Roles.Contains("Member"));

await users.UpdateManyAsync(
    selectedUsers,
    new Dictionary<string, object>
    {
        [nameof(UserEntity.LastActiveUtc)] = DateTime.UtcNow
    });

await users.PullAsync(
    field: x => x.Roles,
    fieldFilter: role => role.StartsWith("Temporary_"));

await users.DeleteManyAsync(
    x => x.LastActiveUtc < DateTime.UtcNow.AddYears(-2));
```

Bulk update and delete transactions require a MongoDB deployment that supports transactions, such as a replica set or sharded cluster. A standalone MongoDB server is not sufficient for those transaction paths.

### Index management

```csharp
using System.Linq.Expressions;

await users.CreateIndexAsync(x => x.Email);
await users.CreateIndexAsync(x => x.LastActiveUtc, TimeSpan.FromDays(30));

Expression<Func<UserEntity, object>>[] fields =
[
    x => x.Email,
    x => x.Name
];

await users.CreateIndexAsync(fields, unique: true);
await users.RemoveIndexAsync(fields);
```

The expiry overload creates a MongoDB TTL index. Use a BSON date field suitable for TTL processing and confirm expiry behavior against the MongoDB server version and deployment policy.

### Mongo advantages

- Natural persistence for document-oriented and nested models.
- Native array update operators and TTL indexes.
- Flexible schema evolution.
- Mature official driver with expression-based filtering.
- Good fit for document-centric horizontal scaling patterns.

### Mongo tradeoffs

- Cross-document transaction behavior depends on deployment topology.
- Relationships and joins require different modeling choices than a relational database.
- Flexible schemas shift more validation responsibility to the application.
- Repository abstractions cannot expose every aggregation-pipeline optimization.
- Collection naming is convention-based unless `CollectionNameAttribute` is used explicitly.

For a focused provider reference, see [Mongo/Mongo/README.md](Mongo/Mongo/README.md).

## Postgres library

### Specification

| Property | Value |
|---|---|
| Project | `Postgres/Postgres/Postgres.csproj` |
| Assembly and package ID | `Postgres` |
| Target framework | `.NET 10` |
| Data access | ADO.NET; no ORM |
| Provider | `Npgsql 10.0.3` |
| Entity contract | `IPostgresEntity` |
| Base entity | `PostgresEntity` |
| Repository contract | `IPostgresRepository<T>` |
| Identifier | Generated GUID represented as a string, maximum mapped length 64 |
| Default storage name | Entity type converted to `snake_case`, with a trailing `Entity` removed |

### Reference the project

```xml
<ItemGroup>
  <ProjectReference Include="..\Postgres\Postgres\Postgres.csproj" />
</ItemGroup>
```

### Define an entity

```csharp
using Postgres.Helpers;

[TableName("ApplicationUsers")]
public sealed class UserEntity : PostgresEntity
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public DateTime LastActiveUtc { get; set; }
}
```

The mapped table name is `application_users`. `PostgresEntity` supplies the same audit and soft-delete members as `MongoEntity`.

### Create a repository

The connection-string constructor is convenient for small tools:

```csharp
using Postgres;

var connectionString = configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Postgres connection string is missing.");

IPostgresRepository<UserEntity> users =
    new PostgresRepository<UserEntity>(connectionString);
```

For hosted applications, register a scoped repository:

```csharp
using Postgres;

services.AddScoped<IPostgresRepository<UserEntity>>(_ =>
    new PostgresRepository<UserEntity>(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Postgres connection string is missing.")));
```

The repository owns one provider connection and is not intended for concurrent operations. Scope it to a request or unit of work; dependency injection disposes the asynchronous repository at the end of the scope.

### CRUD and query example

```csharp
var user = new UserEntity
{
    Name = "Ada Lovelace",
    Email = "ada@example.com",
    Roles = ["Administrator"],
    LastActiveUtc = DateTime.UtcNow
};

await users.InsertAsync(user);

var stored = await users.GetByIdAsync(user.Id);
var exists = await users.ExistsAsync(x => x.Email == "ada@example.com");

stored!.Name = "Augusta Ada King";
await users.UpdateAsync(stored);

await users.DeleteAsync(stored);                    // Soft delete
await users.RestoreAsync(stored.Id);                // Restore
await users.DeleteAsync(stored, hardDelete: true);  // Permanent delete
```

### Filtering, projection, and pagination

```csharp
using Postgres.Helpers;

var summaries = await users.GetAllAsync(
    projection: x => new { x.Id, x.Name, x.Email },
    predicate: x => x.Email.EndsWith("@example.com"));

var page = await users.GetPagedListAsync(
    pageIndex: 1,
    pageSize: 25,
    predicate: x => x.LastActiveUtc >= DateTime.UtcNow.AddDays(-30),
    orderBy: x => x.LastActiveUtc,
    sortDirection: SortDirection.Descending,
    includeDeletes: false);
```

Predicates are translated to parameterized SQL. Entity property comparisons, Boolean composition, null checks, string `StartsWith`/`EndsWith`/`Contains`, and constant-collection `Contains` are supported. Projections run in memory after the matching rows are materialized; unsupported predicates throw `NotSupportedException`.

### Bulk and collection operations

```csharp
var selectedUsers = await users.GetAllAsync(
    predicate: x => x.Email.EndsWith("@example.com"));

await users.UpdateManyAsync(
    selectedUsers,
    new Dictionary<string, object>
    {
        [nameof(UserEntity.LastActiveUtc)] = DateTime.UtcNow
    });

await users.PullAsync(
    field: x => x.Roles,
    fieldFilter: role => role.StartsWith("Temporary_"));

var deleted = await users.DeleteManyAsync(
    x => x.LastActiveUtc < DateTime.UtcNow.AddYears(-2));
```

`PullAsync` loads matching rows, modifies the mapped collection in memory, and saves the changes. For very large sets or provider-optimized array operations, use purpose-built SQL or Npgsql APIs instead.

### Table and index management

```csharp
using System.Linq.Expressions;

await users.EnsureTableAsync();
var exists = await users.TableExistsAsync();

await users.CreateIndexAsync(x => x.Email);

Expression<Func<UserEntity, object>>[] fields =
[
    x => x.Email,
    x => x.Name
];

await users.CreateIndexAsync(fields, unique: true);
await users.RemoveIndexAsync(fields);

// Administrative operation: permanently removes every row.
await users.TruncateTableAsync(restartIdentity: true, cascade: false);
```

`EnsureTableAsync` is useful for demos and isolated tools. Production systems should normally use reviewed, versioned SQL migration scripts so schema changes remain explicit and deployable independently.

PostgreSQL has no native MongoDB-style TTL index. The `expiresAfter` overload creates a normal index; automated expiry requires an application job, database scheduler, or another retention mechanism.

### Postgres advantages

- Strong relational integrity, constraints, and transactional semantics.
- Excellent SQL tooling and reporting ecosystem.
- Low-overhead, parameterized ADO.NET access through Npgsql.
- Natural support for relational modeling and joins.
- Transaction-backed bulk mutation paths in the repository.

### Postgres tradeoffs

- Schema changes require disciplined migration management.
- The built-in SQL translator intentionally supports a focused subset of .NET expressions.
- A repository instance owns one connection and is not intended for concurrent use.
- Collection pulling is less efficient than MongoDB's native array update operator.
- TTL behavior is not native, and partial-index filter translation is not implemented by this repository.

For a focused provider reference, see [Postgres/README.md](Postgres/README.md).

## Sqlite library

### Specification

| Property | Value |
|---|---|
| Project | `Sqlite/Sqlite/Sqlite.csproj` |
| Assembly and package ID | `Sqlite` |
| Target framework | `.NET 10` |
| Data access | ADO.NET; no ORM |
| Provider | `Microsoft.Data.Sqlite.Core 10.0.10` |
| Entity contract | `ISqliteEntity` |
| Base entity | `SqliteEntity` |
| Repository contract | `ISqliteRepository<T>` |
| Identifier | Generated GUID represented as a string, maximum mapped length 64 |
| Default storage name | Entity type converted to `snake_case`, with a trailing `Entity` removed |

### Reference the project

```xml
<ItemGroup>
  <ProjectReference Include="..\Sqlite\Sqlite\Sqlite.csproj" />
</ItemGroup>
```

### Define an entity

```csharp
using Sqlite.Helpers;

[TableName("ApplicationUsers")]
public sealed class UserEntity : SqliteEntity
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public DateTime LastActiveUtc { get; set; }
}
```

The mapped table name is `application_users`. `SqliteEntity` supplies the same audit and soft-delete members as `MongoEntity` and `PostgresEntity`.

### Create a repository

The connection-string constructor is convenient for small tools:

```csharp
using Sqlite;

ISqliteRepository<UserEntity> users =
    new SqliteRepository<UserEntity>("Data Source=application.db");
```

For hosted applications, register a scoped repository:

```csharp
using Sqlite;

services.AddScoped<ISqliteRepository<UserEntity>>(_ =>
    new SqliteRepository<UserEntity>(
        configuration.GetConnectionString("Sqlite")
        ?? throw new InvalidOperationException("Sqlite connection string is missing.")));
```

The repository owns one SQLite connection and implements `IAsyncDisposable`. Scope it to a request or unit of work, or use `await using` when deterministic file release is required.

### CRUD, queries, and workflows

The SQLite repository exposes the same API surface as the PostgreSQL repository: `InsertAsync`, `GetByIdAsync`, `ExistsAsync`, `UpdateAsync`, `UpdateManyAsync`, `PullAsync`, `DeleteAsync`/`DeleteByIdAsync`/`DeleteOneAsync`/`DeleteManyAsync`, `RestoreAsync`, `GetAllAsync` (with LINQ projections), `GetPagedListAsync` (single- and multi-field sorting), `CountAsync`, `GetFirstOrDefaultAsync`, and `GetSingleOrDefaultAsync`. All PostgreSQL usage examples above apply verbatim after swapping the `Postgres` types and namespace for `Sqlite`.

### Table and index management

```csharp
using System.Linq.Expressions;

await users.EnsureTableAsync();
var exists = await users.TableExistsAsync();

await users.CreateIndexAsync(x => x.Email);

Expression<Func<UserEntity, object>>[] fields =
[
    x => x.Email,
    x => x.Name
];

await users.CreateIndexAsync(fields, unique: true);
await users.RemoveIndexAsync(fields);

// Administrative operation: permanently removes every row via unfiltered DELETE.
await users.TruncateTableAsync(restartIdentity: true);
```

`TableExistsAsync` queries `sqlite_master`; the optional `schema` argument is reserved for API parity and ignored. SQLite has no `TRUNCATE` statement, so `TruncateTableAsync` issues an unfiltered `DELETE` and, when `restartIdentity` is set, resets the table's `AUTOINCREMENT` counter in `sqlite_sequence`. The `cascade` argument is accepted for API parity but has no effect. Like PostgreSQL, the TTL overload creates a normal index, and the partial-index filter argument is reserved.

### Sqlite advantages

- Zero infrastructure: embedded, serverless, and credential-free.
- Single-file databases that are trivial to back up, copy, and ship.
- Low-overhead, parameterized ADO.NET access through Microsoft.Data.Sqlite.
- Ideal for tests, demos, desktop or mobile apps, and offline-first workloads.
- Transaction-backed bulk mutation paths in the repository.

### Sqlite tradeoffs

- Single-writer concurrency; concurrent writes serialize at the database level.
- No network layer; every consumer needs access to the database file.
- Truncation is a `DELETE`-based reset, not a constant-time `TRUNCATE`.
- Collection pulling is less efficient than MongoDB's native array update operator.
- TTL behavior is not native, and partial-index filter translation is not implemented by this repository.

For a focused provider reference, see [Sqlite/README.md](Sqlite/README.md).

## Shared behavior

### Audit fields

All three base entities expose:

| Property | Behavior |
|---|---|
| `Id` | Generated when the entity instance is created |
| `CreatedDateTime` | Set to `DateTime.UtcNow` on insert |
| `UpdatedDateTime` | Set to `DateTime.UtcNow` on update and collection mutation |
| `DeletedDateTime` | Set by a soft delete and cleared by restore |
| `IsDeleted` | Computed as `DeletedDateTime != null` |

### Soft-delete query semantics

The following operations exclude soft-deleted records unless their `includeDeletes` or `includeDeleted` argument is set to `true`:

- `GetAllAsync`
- projection queries
- `GetPagedListAsync`
- `GetFirstOrDefaultAsync`
- `GetByIdAsync`
- `ExistsAsync`

`CountAsync` and `GetSingleOrDefaultAsync` apply the predicate exactly as supplied and do not add an implicit soft-delete predicate. Include `x => x.DeletedDateTime == null` when active-only behavior is required for those methods.

### Pagination

`IPagedList<T>` provides:

- `PageIndex`
- `PageSize`
- `TotalCount`
- `TotalPages`
- `HasPreviousPage`
- `HasNextPage`
- `Items`

Page indexes are one-based. The PostgreSQL and SQLite repositories validate both `pageIndex` and `pageSize` as positive values. Always supply deterministic ordering when records can share the default creation timestamp.

### Sensitive data marker

`SensitiveDataAttribute` is metadata only. It does not encrypt, hash, redact, or obfuscate a property by itself. Applications must implement the required protection in serialization, persistence, logging, or domain services.

## Console applications

The console projects are executable specifications and smoke-test demonstrations. They never contain committed database credentials. The Mongo and Postgres apps each start an ephemeral Docker container, obtain their runtime connection string from Testcontainers, perform the demonstration, and remove the container through `await using` when the process exits normally. The Sqlite app needs no Docker at all: it creates an ephemeral database file in the system temp directory and deletes it on exit.

### Requirements

- .NET 10 SDK (all three apps)
- For Mongo.Console and Postgres.Console only:
  - A Docker-compatible engine
  - Permission to pull images and create containers
  - The Docker endpoint available to Testcontainers

On Windows, start Docker Desktop before running the Mongo or Postgres app. A `DockerUnavailableException` means the Docker engine is stopped or its endpoint is not accessible. Sqlite.Console runs everywhere the .NET SDK runs.

### Mongo.Console

| Property | Value |
|---|---|
| Project | `Mongo/Mongo.Console/Mongo.Console.csproj` |
| Testcontainers package | `Testcontainers.MongoDb 4.13.0` |
| Docker image | `mongo:6.0` |
| Deployment | Single-node replica set (`rs0`) for transactional bulk operations |
| Database | `repository_demo` |
| Collection | `feature_examples` |
| Persistent external state | None |

Run it with:

```powershell
dotnet run --project Mongo/Mongo.Console/Mongo.Console.csproj
```

The application is a self-checking executable specification. Every successful behavior prints an `[OK]` line; a failed expectation throws and terminates the run. It demonstrates:

1. `CollectionNameAttribute`, `SensitiveDataAttribute`, and base audit metadata without printing sensitive values.
2. Simple, native TTL, compound unique partial, and directional compound indexes.
3. Single and bulk insert with automatic creation timestamps.
4. Count, existence, ID lookup, first-or-default, single-or-default, filtered lists, sorting, and projection.
5. Single-field and multi-field pagination, totals, and navigation flags.
6. Single replacement update and replica-set-backed transactional `UpdateManyAsync`.
7. Both collection removal APIs: the lower-level `FieldDefinition` overload and typed predicate overload.
8. Soft deletion through ID, entity, predicate, and collection workflows.
9. Default soft-delete filtering and opt-in reads with `includeDeletes: true`.
10. Restore by ID and entity.
11. Permanent deletion through the single and bulk APIs.
12. Removal of every demonstration index.
13. Automatic disposal of the replica set and all data.

TTL documents use future expiry timestamps so the demonstration does not race MongoDB's background TTL monitor. The `SensitiveDataAttribute` value is deliberately never printed.

### Postgres.Console

| Property | Value |
|---|---|
| Project | `Postgres/Postgres.Console/Postgres.Console.csproj` |
| Testcontainers package | `Testcontainers.PostgreSql 4.13.0` |
| Docker image | `postgres:15.1` |
| Table | `feature_examples` |
| Persistent external state | None |

Run it with:

```powershell
dotnet run --project Postgres/Postgres.Console/Postgres.Console.csproj
```

The application is a self-checking executable specification. Every successful behavior prints an `[OK]` line; a failed expectation throws and terminates the run. It demonstrates:

1. `TableNameAttribute`, `SensitiveDataAttribute`, and base audit metadata without printing sensitive values.
2. Mapped, named, and schema-qualified table existence checks.
3. Runtime table creation and deterministic truncation.
4. Simple, TTL-overload, compound unique, and directional compound indexes.
5. The provider distinction that TTL creates a standard index and partial-filter translation is currently reserved.
6. Single and bulk insert with automatic creation timestamps.
7. Count, existence, ID lookup, first-or-default, single-or-default, filtered lists, sorting, and LINQ projection.
8. Single-field and multi-field pagination, totals, and navigation flags.
9. Single update and transaction-backed `UpdateManyAsync`.
10. Collection-value removal through ADO.NET read/modify/write `PullAsync`.
11. Soft deletion through ID, entity, predicate, and collection workflows.
12. Default soft-delete filtering and opt-in reads with `includeDeletes: true`.
13. Restore by ID and entity.
14. Permanent deletion through the single and bulk APIs.
15. Removal of every demonstration index.
16. Automatic disposal of the database and all data.

### Sqlite.Console

| Property | Value |
|---|---|
| Project | `Sqlite/Sqlite.Console/Sqlite.Console.csproj` |
| External dependencies | None; embedded SQLite database file |
| Database | Ephemeral file in the system temp directory |
| Table | `feature_examples` |
| Persistent external state | None |

Run it with:

```powershell
dotnet run --project Sqlite/Sqlite.Console/Sqlite.Console.csproj
```

The application is a self-checking executable specification. Every successful behavior prints an `[OK]` line; a failed expectation throws and terminates the run. It demonstrates:

1. `TableNameAttribute`, `SensitiveDataAttribute`, and base audit metadata without printing sensitive values.
2. Mapped and named table existence checks against `sqlite_master`, including the reserved schema argument.
3. Runtime table creation and the `DELETE`-based truncate reset.
4. Simple, TTL-overload, compound unique, and directional compound indexes.
5. The provider distinction that TTL creates a standard index and partial-filter translation is currently reserved.
6. Single and bulk insert with automatic creation timestamps.
7. Count, existence, ID lookup, first-or-default, single-or-default, filtered lists, sorting, and LINQ projection.
8. Single-field and multi-field pagination, totals, and navigation flags.
9. Single update and transaction-backed `UpdateManyAsync`.
10. Collection-value removal through ADO.NET read/modify/write `PullAsync`.
11. Soft deletion through ID, entity, predicate, and collection workflows.
12. Default soft-delete filtering and opt-in reads with `includeDeletes: true`.
13. Restore by ID and entity.
14. Permanent deletion through the single and bulk APIs.
15. Removal of every demonstration index.
16. Disposal of the repository and deletion of the database file.

The console applications are demonstrations, not benchmarks. Image tags are pinned for repeatability; update them deliberately after validating database compatibility.

## Benchmarks

The permanent BenchmarkDotNet projects exercise the public repository APIs against 1,000 deterministic records. MongoDB runs in a `mongo:6.0` Testcontainer configured as a single-node replica set, and PostgreSQL runs in a `postgres:15.1` Testcontainer. Container startup, schema/index creation, and record seeding happen before BenchmarkDotNet starts the timed workload.

SQLite is an embedded database rather than a database server, so there is no SQLite Testcontainer connection endpoint for the repository to use. Its equivalent benchmark creates an isolated temporary database file, seeds it, runs the measurements through `Microsoft.Data.Sqlite`, and deletes the file afterward.

Each provider measures the same operations:

- `GetByIdAsync`: retrieve one record by its primary identifier.
- `GetFilteredAsync`: materialize and sort the 500 records whose category is `Even`.
- `GetPageAsync`: retrieve page 10 with 25 records from that filtered and sorted result, including the total count.
- `CountFilteredAsync`: count the 500 records whose category is `Even`.

### Measured results

Measured on 2026-07-30 with BenchmarkDotNet 0.15.8, .NET 10.0.10, Windows 11, and an Intel Core i7-10875H CPU. Values are per repository call. Allocations are managed allocations reported by `MemoryDiagnoser`.

Within each method, providers are ordered fastest to slowest.

| Method | Provider | Mean | StdDev | Allocated |
|---|---|---:|---:|---:|
| `GetByIdAsync` | SQLite | 0.1334 ms | 0.0147 ms | 10.59 KB |
| `GetByIdAsync` | PostgreSQL | 0.7962 ms | 0.0661 ms | 9.92 KB |
| `GetByIdAsync` | MongoDB | 1.206 ms | 0.1134 ms | 59.31 KB |
| `GetFilteredAsync` | PostgreSQL | 2.6027 ms | 0.2369 ms | 727.23 KB |
| `GetFilteredAsync` | SQLite | 4.5928 ms | 0.3640 ms | 943.04 KB |
| `GetFilteredAsync` | MongoDB | 6.797 ms | 0.4948 ms | 680.79 KB |
| `GetPageAsync` | SQLite | 1.3563 ms | 0.1812 ms | 61.32 KB |
| `GetPageAsync` | PostgreSQL | 1.8496 ms | 0.0831 ms | 50.64 KB |
| `GetPageAsync` | MongoDB | 3.844 ms | 0.3168 ms | 94.11 KB |
| `CountFilteredAsync` | SQLite | 0.1495 ms | 0.0245 ms | 4.20 KB |
| `CountFilteredAsync` | PostgreSQL | 0.8560 ms | 0.0606 ms | 4.83 KB |
| `CountFilteredAsync` | MongoDB | 1.141 ms | 0.1033 ms | 28.17 KB |

These numbers describe one machine and one local container configuration; they are a reproducible baseline, not a universal database ranking. MongoDB and PostgreSQL include their real driver and local container network paths, while SQLite executes in-process against a local file.

Run the complete benchmark projects in Release mode:

```powershell
dotnet run --project Mongo/Mongo.Benchmark/Mongo.Benchmark.csproj -c Release -- --filter "*" --noOverwrite
dotnet run --project Postgres/Postgres.Benchmark/Postgres.Benchmark.csproj -c Release -- --filter "*" --noOverwrite
dotnet run --project Sqlite/Sqlite.Benchmark/Sqlite.Benchmark.csproj -c Release -- --filter "*" --noOverwrite
```

Use `--job Dry` before a full run when validating environment or code changes. BenchmarkDotNet writes the detailed Markdown and CSV reports under each project's `BenchmarkDotNet.Artifacts` directory.

## Testing and coverage

| Test project | Framework | Current result | External database required |
|---|---|---:|---:|
| `Mongo.Tests` | NUnit 4.6.1, Moq 4.20.72 | 42 passing tests | No |
| `Postgres.Tests` | NUnit 4.6.1, Microsoft.Data.Sqlite | 32 passing tests | No |
| `Sqlite.Tests` | NUnit 4.6.1, Microsoft.Data.Sqlite | 32 passing tests | No |

The latest solution run passed all 106 tests.

### Test coverage

Coverage was collected on 2026-07-30 from all 106 passing tests with Coverlet's Cobertura collector. Test and benchmark assemblies are excluded, and automatically generated property accessors are skipped.

| Production library | Line coverage | Branch coverage | Covered lines | Covered branches |
|---|---:|---:|---:|---:|
| MongoDB | 33.50% | 28.40% | 193 / 576 | 50 / 176 |
| PostgreSQL | 78.42% | 66.72% | 854 / 1,089 | 359 / 538 |
| SQLite | 78.53% | 66.79% | 860 / 1,095 | 358 / 536 |
| **Overall** | **69.09%** | **61.36%** | **1,907 / 2,760** | **767 / 1,250** |

Regenerate the raw Cobertura reports:

```powershell
dotnet test Monorepo.sln `
  --collect:"XPlat Code Coverage" `
  --results-directory TestResults/coverage-analysis/raw
```

Run all tests:

```powershell
dotnet test Monorepo.sln
```

Collect PostgreSQL or SQLite coverage:

```powershell
dotnet test Postgres/Postgres.Tests/Postgres.Tests.csproj `
  --collect:"XPlat Code Coverage"

dotnet test Sqlite/Sqlite.Tests/Sqlite.Tests.csproj `
  --collect:"XPlat Code Coverage"
```

The relational test suites use process-local SQLite connections to cover ADO.NET behavior without requiring an external database or Docker.

## Technical specifications

| Component | Version |
|---|---:|
| .NET target framework | 10.0 |
| MongoDB.Driver / MongoDB.Bson | 3.10.0 |
| Npgsql | 10.0.3 |
| Microsoft.Data.Sqlite.Core | 10.0.10 |
| Testcontainers MongoDB / PostgreSQL | 4.13.0 |
| BenchmarkDotNet | 0.15.8 |
| NUnit | 4.6.1 |
| NUnit3TestAdapter | 6.2.0 |
| Microsoft.NET.Test.Sdk | 18.8.1 |
| coverlet.collector | 10.0.1 |

At the time of the latest dependency audit, NuGet reported no outdated top-level packages and no known vulnerable top-level or transitive packages.

## Production guidance

### Dependency injection and lifetimes

- Reuse `MongoClient`; it is designed to be long-lived and manages connection pooling.
- Scope `PostgresRepository<T>` and `SqliteRepository<T>` to one request or unit of work.
- Do not execute concurrent commands through the same relational repository instance.
- Prefer constructor injection through `IMongoRepository<T>`, `IPostgresRepository<T>`, or `ISqliteRepository<T>`.

### Secrets

- Never commit connection strings, passwords, certificates, or access tokens.
- Use environment variables, development user-secrets, or a managed secret store.
- Rotate any credential that has previously been committed; deleting it from the current tree does not remove it from Git history.
- Testcontainers connection strings are generated at runtime and should not be copied into configuration.

### Schema and indexes

- Use reviewed migrations for PostgreSQL production schema changes.
- Treat runtime index creation as an administrative operation.
- Validate index choices with real workload measurements and query plans.
- Use projections and pagination to control network and allocation costs.

### Error handling

- Repository methods surface provider and translation exceptions unless a method explicitly documents recovery behavior.
- Add application-level logging, retries, and cancellation policies appropriate to the deployment.
- Treat bulk-operation failure as a unit-of-work failure and verify the final persisted state.

## Known limitations

- The libraries provide similar workflows, not identical database semantics.
- PostgreSQL and SQLite partial-index expression translation is not implemented.
- PostgreSQL and SQLite TTL overloads create standard indexes only.
- PostgreSQL and SQLite `PullAsync` is a read/modify/write operation and may be expensive for large result sets.
- SQLite truncation is an unfiltered `DELETE`; the `cascade` argument is accepted for parity and ignored.
- SQLite serializes concurrent writes and offers no network access layer.
- `SensitiveDataAttribute` does not perform protection automatically.
- Each generic PostgreSQL and SQLite repository maps one entity type.
- Runtime table creation is useful for demos but is not a replacement for production migrations.
- The Mongo and Postgres console applications require Docker and cannot run while the Docker engine is unavailable; the Sqlite console application has no such requirement.

## Build commands

Restore and build everything:

```powershell
dotnet restore Monorepo.sln
dotnet build Monorepo.sln --no-restore
```

Build individual libraries:

```powershell
dotnet build Mongo/Mongo/Mongo.csproj
dotnet build Postgres/Postgres/Postgres.csproj
dotnet build Sqlite/Sqlite/Sqlite.csproj
```

Run dependency checks:

```powershell
dotnet list Monorepo.sln package --outdated
dotnet list Monorepo.sln package --vulnerable --include-transitive
```

## Contributing

1. Keep provider-specific behavior explicit.
2. Add tests for every public behavior and edge case changed.
3. Run the full solution build and test suite.
4. Collect coverage when changing the PostgreSQL or SQLite production assemblies.
5. Update this README when APIs, dependencies, images, or operational requirements change.

Copyright 2026 Devspace.
