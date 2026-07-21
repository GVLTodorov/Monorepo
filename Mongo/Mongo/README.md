# Mongo repository library

`Mongo` is a strongly typed repository abstraction over the official MongoDB .NET driver. It provides a consistent application-facing API for CRUD, audit fields, soft deletion, projections, pagination, collection updates, and index management while preserving MongoDB-specific capabilities such as TTL and partial indexes.

For the side-by-side MongoDB/PostgreSQL comparison and solution-wide guidance, see the [root documentation](../../README.md).

## Technical specification

| Area | Specification |
|---|---|
| Target framework | .NET 10 (`net10.0`) |
| Package and assembly | `Mongo` |
| Database provider | MongoDB.Driver 3.10.0 |
| Identifier | MongoDB `ObjectId` represented as a string |
| Data access | MongoDB driver expressions and definitions |
| Collection naming | Lowercase type or `[CollectionName]` value with `entity` removed |
| Audit behavior | Automatic created, updated, deleted, and soft-delete fields |

## When to use it

Choose this library when documents naturally contain nested objects or collections, the schema needs to evolve easily, or the application benefits from MongoDB-native array updates and TTL indexes. PostgreSQL is normally a better fit when relational constraints, joins, or multi-table transactions are central to the model.

### Advantages

- Small repository API over the official driver.
- Flexible document schema and natural nested-data storage.
- Native TTL, unique, compound, directional, and partial indexes.
- Server-side `PullAsync` operations for array fields.
- Expression-based filtering, sorting, and strongly typed entities.
- Built-in audit timestamps, soft delete, restore, projection, and pagination.

### Tradeoffs

- Relational integrity and joins are not the document model's strengths.
- Transactions require a replica set or sharded deployment; a standalone MongoDB server does not support them.
- `GetAllAsync` can materialize an unbounded result and should not be used for large collections.
- `[SensitiveData]` is metadata only; it does not encrypt, redact, or omit a value automatically.
- Collection naming removes every lowercase occurrence of `entity`, so explicit names are safest when convention output could be ambiguous.

## Installation

Reference the project while developing in this monorepo:

```xml
<ProjectReference Include="..\path\to\Mongo\Mongo\Mongo.csproj" />
```

When consuming a packed release, reference the package version produced by the repository:

```xml
<PackageReference Include="Mongo" Version="VERSION" />
```

## Define an entity

Entities must inherit `MongoEntity` or implement `IMongoEntity`.

```csharp
using Mongo;
using Mongo.Helpers;

[CollectionName("users")]
public sealed class UserEntity : MongoEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];

    [SensitiveData]
    public string PasswordHash { get; set; } = string.Empty;
}
```

`MongoEntity` supplies `Id`, `CreatedDateTime`, `UpdatedDateTime`, `DeletedDateTime`, and `IsDeleted`. New IDs and audit values are managed by the repository.

## Create and register a repository

Create a repository directly:

```csharp
var repository = new MongoRepository<UserEntity>(
    connectionString,
    database: "application");
```

Or share a singleton `MongoClient` and create scoped repositories:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Mongo;
using MongoDB.Driver;

services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
services.AddScoped<IMongoRepository<UserEntity>>(provider =>
    new MongoRepository<UserEntity>(
        provider.GetRequiredService<IMongoClient>(),
        database: "application"));
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
    [nameof(UserEntity.UpdatedDateTime)] = DateTime.UtcNow
});
```

The update dictionary uses CLR property names. Invalid or incompatible values fail at runtime, so prefer `nameof` over string literals.

## Query, sort, and page

```csharp
using Mongo.Helpers;

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

Mongo projections use the driver's `ProjectionDefinition<T, TProjection>`:

```csharp
using MongoDB.Driver;

var projection = Builders<UserEntity>.Projection.Expression(user => new
{
    user.Id,
    user.Name,
    user.Email
});

var summaries = await repository.GetAllAsync(
    projection,
    predicate: user => user.Roles.Contains("Admin"));
```

Projection limits the fields transferred from the server and is preferable when the complete document is unnecessary.

## Remove collection values

The typed overload removes matching array values on the server:

```csharp
await repository.PullAsync(
    field: user => user.Roles,
    fieldFilter: role => role == "Temporary",
    documentPredicate: user => user.Email.EndsWith("@example.test"));
```

The lower-level overload accepts a MongoDB `FieldDefinition<T>` and a value. It can optionally target selected entities and require another field to exist.

## Indexes

```csharp
await repository.CreateIndexAsync(user => user.Email);

await repository.CreateIndexAsync(
    fields: new Expression<Func<UserEntity, object>>[]
    {
        user => user.Email,
        user => user.Name
    },
    filter: user => !user.IsDeleted,
    unique: true);

await repository.CreateIndexAsync(
    user => user.DeletedDateTime,
    expiresAfter: TimeSpan.FromDays(30));

await repository.RemoveIndexAsync(user => user.Email);
```

TTL indexes require a BSON date field and MongoDB removes expired documents asynchronously. Validate a TTL policy carefully because it performs physical deletion independently of repository soft-delete behavior.

## Soft-delete behavior

List, paging, first, projection, and existence operations exclude soft-deleted documents by default. Pass the applicable `includeDeletes` or `includeDeleted` argument when an API provides it and deleted documents must be returned. Restore clears the deletion state; hard delete permanently removes a document.

`CountAsync` and `GetSingleOrDefaultAsync` execute their supplied predicates, so include `!entity.IsDeleted` explicitly when counting or selecting only active records.

## Console demonstration

The sibling console app is a self-checking executable specification for the complete repository surface. It starts `mongo:6.0` as a single-node replica set named `rs0`, which allows the transactional bulk update and delete methods to run. It contains no static connection string or database credential.

Prerequisites:

- .NET 10 SDK.
- Docker Desktop or another Docker-compatible engine running locally.
- Permission to pull the image on the first run.

Run it from the repository root:

```powershell
dotnet run --project Mongo/Mongo.Console/Mongo.Console.csproj
```

The container and its data are disposed when the process exits.

The demonstration covers:

1. Collection-name and sensitive-data attributes without printing sensitive values.
2. Simple, TTL, compound unique partial, and directional indexes, followed by removal.
3. Single and bulk insert plus automatic audit timestamps.
4. Count, existence, ID, first, single, filtered, sorted, and projected reads.
5. Single-field and multi-field pagination with totals and navigation flags.
6. Single update and transactional bulk update.
7. Both `PullAsync` overloads.
8. Soft delete, default filtering, include-deleted reads, and both restore overloads.
9. ID, entity, predicate, and collection deletion APIs in soft and hard-delete workflows.

Each successful check prints `[OK]`. Any unexpected result throws an exception and makes the process fail. TTL timestamps are set seven days in the future so the background TTL monitor cannot remove demonstration documents during the run.

## Tests

```powershell
dotnet test Mongo/Mongo.Tests/Mongo.Tests.csproj
```

The Mongo suite currently contains 42 tests. Add focused tests for every public behavior or edge case changed.

## Operational guidance

- Reuse `MongoClient`; it owns connection pooling and is designed for reuse.
- Index fields used by common predicates and sorts, but avoid redundant indexes.
- Prefer projections and pagination for large data sets.
- Use UTC timestamps consistently.
- Do not share mutable entity instances across concurrent operations without synchronization.
- Treat hard deletes and TTL policies as irreversible production operations.

Copyright 2026 Devspace.
