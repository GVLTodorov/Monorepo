# Devspace data repositories

Production-oriented repository abstractions for MongoDB and PostgreSQL on .NET 10.

This solution provides two strongly typed libraries with intentionally similar workflows for CRUD, bulk operations, audit timestamps, soft deletion, restore, projections, sorting, pagination, collection updates, and index management. Provider-specific behavior remains available where the databases differ.

## Contents

- [Projects](#projects)
- [Choose a database](#choose-a-database)
- [Feature comparison](#feature-comparison)
- [Mongo library](#mongo-library)
- [Postgres library](#postgres-library)
- [Shared behavior](#shared-behavior)
- [Console applications](#console-applications)
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
|   |-- Mongo.Console/          MongoDB Testcontainers demonstration
|   `-- Mongo.Tests/            NUnit unit tests
|-- Postgres/
|   |-- Postgres/               PostgreSQL repository library
|   |-- Postgres.Console/       PostgreSQL Testcontainers CRUD demonstration
|   `-- Postgres.Tests/         NUnit unit and provider-level tests
|-- Monorepo.sln
`-- README.md
```

All six projects target `net10.0`. The two library projects are packable and generate XML API documentation.

## Choose a database

| Choose MongoDB when | Choose PostgreSQL when |
|---|---|
| Documents have flexible or evolving shapes | The domain benefits from a strongly defined relational schema |
| Nested objects and arrays are central to the model | Foreign keys, constraints, and relational integrity are important |
| Native document TTL indexes are required | Multi-row ACID transactions are a primary requirement |
| The workload naturally maps to document access patterns | SQL reporting, joins, and established relational tooling matter |
| Horizontal document distribution is expected | Complex relational querying and EF Core integration are preferred |

The repository APIs reduce application-level differences, but they do not make the databases interchangeable. Schema design, indexing, transaction behavior, query translation, and deployment topology must still be designed for the selected provider.

## Feature comparison

| Capability | Mongo | Postgres |
|---|---:|---:|
| Strongly typed predicates | Yes | Yes, translated by EF Core |
| Single and bulk insert | Yes | Yes |
| Single and bulk update | Yes | Yes |
| Soft and hard delete | Yes | Yes |
| Restore soft-deleted records | Yes | Yes |
| Projection | MongoDB `ProjectionDefinition` | LINQ expression |
| One- and multi-column sorting | Yes | Yes |
| Pagination metadata | Yes | Yes |
| Collection item removal | Native MongoDB pull operation | EF Core read/modify/write |
| Simple, compound, directional, and unique indexes | Yes | Yes |
| Partial indexes | Yes | Not implemented; the current filter argument is reserved |
| TTL indexes | Native | Not native; the TTL overload creates a normal index |
| Automatic storage creation | MongoDB creates collections on first write | Repository can ensure the mapped table exists |
| Table truncation | Not applicable | Yes, with identity restart and cascade options |
| Provider-level transactions for bulk mutation | Requires a transaction-capable MongoDB deployment | Relational EF Core transaction |

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
| ORM | `Microsoft.EntityFrameworkCore 10.0.10` |
| Provider | `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` |
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

The direct connection-string constructor is convenient for small tools:

```csharp
using Postgres;

var connectionString = configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Postgres connection string is missing.");

IPostgresRepository<UserEntity> users =
    new PostgresRepository<UserEntity>(connectionString);
```

For hosted applications, prefer dependency injection and a scoped context:

```csharp
using Microsoft.EntityFrameworkCore;
using Postgres;

services.AddDbContext<PostgresDbContext<UserEntity>>(options =>
    options.UseNpgsql(configuration.GetConnectionString("Postgres")));

services.AddScoped<IPostgresRepository<UserEntity>, PostgresRepository<UserEntity>>();
```

`DbContext` is not thread-safe. Use a scoped repository/context per request or unit of work, and never execute concurrent operations through the same instance.

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
    predicate: x => x.Roles.Contains("Administrator"));

var page = await users.GetPagedListAsync(
    pageIndex: 1,
    pageSize: 25,
    predicate: x => x.LastActiveUtc >= DateTime.UtcNow.AddDays(-30),
    orderBy: x => x.LastActiveUtc,
    sortDirection: SortDirection.Descending,
    includeDeletes: false);
```

Predicates and projections must be translatable by the configured EF Core provider. Translation failures are reported at query execution time.

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

`EnsureTableAsync` is useful for demos and isolated tools. Production systems should normally use reviewed EF Core migrations so schema changes are explicit, versioned, and deployable independently.

PostgreSQL has no native MongoDB-style TTL index. The `expiresAfter` overload creates a normal index; automated expiry requires an application job, database scheduler, or another retention mechanism.

### Postgres advantages

- Strong relational integrity, constraints, and transactional semantics.
- Excellent SQL tooling and reporting ecosystem.
- EF Core change tracking and LINQ integration.
- Natural support for relational modeling and joins.
- Transaction-backed bulk mutation paths in the repository.

### Postgres tradeoffs

- Schema changes require disciplined migration management.
- EF Core query translation limits which .NET expressions can execute server-side.
- `DbContext` and the repository instance are not thread-safe.
- Collection pulling is less efficient than MongoDB's native array update operator.
- TTL behavior is not native, and partial-index filter translation is not implemented by this repository.

For a focused provider reference, see [Postgres/README.md](Postgres/README.md).

## Shared behavior

### Audit fields

Both base entities expose:

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

Page indexes are one-based. PostgreSQL validates both `pageIndex` and `pageSize` as positive values. Always supply deterministic ordering when records can share the default creation timestamp.

### Sensitive data marker

`SensitiveDataAttribute` is metadata only. It does not encrypt, hash, redact, or obfuscate a property by itself. Applications must implement the required protection in serialization, persistence, logging, or domain services.

## Console applications

The console projects are executable specifications and smoke-test demonstrations. They never contain committed database credentials. Each starts an ephemeral Docker container, obtains its runtime connection string from Testcontainers, performs the demonstration, and removes the container through `await using` when the process exits normally.

### Requirements

- .NET 10 SDK
- A Docker-compatible engine
- Permission to pull images and create containers
- The Docker endpoint available to Testcontainers

On Windows, start Docker Desktop before running either app. A `DockerUnavailableException` means the Docker engine is stopped or its endpoint is not accessible.

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
10. Collection-value removal through EF Core read/modify/write `PullAsync`.
11. Soft deletion through ID, entity, predicate, and collection workflows.
12. Default soft-delete filtering and opt-in reads with `includeDeletes: true`.
13. Restore by ID and entity.
14. Permanent deletion through the single and bulk APIs.
15. Removal of every demonstration index.
16. Automatic disposal of the database and all data.

The console applications are demonstrations, not benchmarks. Image tags are pinned for repeatability; update them deliberately after validating database compatibility.

## Testing and coverage

| Test project | Framework | Current result | External database required |
|---|---|---:|---:|
| `Mongo.Tests` | NUnit 4.6.1, Moq 4.20.72 | 42 passing tests | No |
| `Postgres.Tests` | NUnit 4.6.1, SQLite/InMemory EF providers | 65 passing tests | No |

The PostgreSQL production assembly is measured at:

- 100% line coverage: 581/581
- 100% branch coverage: 202/202

Run all tests:

```powershell
dotnet test Monorepo.sln
```

Collect PostgreSQL coverage:

```powershell
dotnet test Postgres/Postgres.Tests/Postgres.Tests.csproj `
  --collect:"XPlat Code Coverage"
```

The test suite uses process-local SQLite and EF Core InMemory providers to cover repository behavior and provider routing without requiring PostgreSQL or Docker.

## Technical specifications

| Component | Version |
|---|---:|
| .NET target framework | 10.0 |
| MongoDB.Driver / MongoDB.Bson | 3.10.0 |
| Entity Framework Core | 10.0.10 |
| Npgsql EF Core provider | 10.0.3 |
| Testcontainers MongoDB / PostgreSQL | 4.13.0 |
| NUnit | 4.6.1 |
| NUnit3TestAdapter | 6.2.0 |
| Microsoft.NET.Test.Sdk | 18.8.1 |
| coverlet.collector | 10.0.1 |

At the time of the latest dependency audit, NuGet reported no outdated direct packages and no known vulnerable direct or transitive packages.

## Production guidance

### Dependency injection and lifetimes

- Reuse `MongoClient`; it is designed to be long-lived and manages connection pooling.
- Scope `PostgresDbContext<T>` and `PostgresRepository<T>` to one request or unit of work.
- Do not share an EF Core repository/context between threads.
- Prefer constructor injection through `IMongoRepository<T>` or `IPostgresRepository<T>`.

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
- PostgreSQL partial-index expression translation is not implemented.
- PostgreSQL TTL overloads create standard indexes only.
- PostgreSQL `PullAsync` is a read/modify/write operation and may be expensive for large result sets.
- `SensitiveDataAttribute` does not perform protection automatically.
- The generic PostgreSQL context models one entity type per closed generic context.
- Runtime table creation is useful for demos but is not a replacement for production migrations.
- Console applications require Docker and cannot run while the Docker engine is unavailable.

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
4. Collect coverage when changing the PostgreSQL production assembly.
5. Update this README when APIs, dependencies, images, or operational requirements change.

Copyright 2026 Devspace.
