using System.Linq.Expressions;
using Postgres.Helpers;
using Testcontainers.PostgreSql;
using SortDirection = Postgres.Helpers.SortDirection;

namespace Postgres.Console;

public static class Program
{
    public static async Task Main()
    {
        Banner("PostgreSQL repository feature demonstration");
        System.Console.WriteLine("Starting an ephemeral PostgreSQL database with Testcontainers...");

        await using var postgres = new PostgreSqlBuilder("postgres:15.1").Build();
        await postgres.StartAsync();

        var repository = new PostgresRepository<ExampleEntity>(postgres.GetConnectionString());

        ShowMetadata();
        await DemonstrateTableLifecycleAsync(repository);
        await DemonstrateIndexesAsync(repository);

        var entities = CreateEntities();
        await DemonstrateCreateAndReadAsync(repository, entities);
        await DemonstrateUpdatesAndPullsAsync(repository, entities);
        await DemonstrateDeletesAndRestoreAsync(repository, entities);
        await RemoveIndexesAsync(repository);

        Section("COMPLETE");
        System.Console.WriteLine("Every public PostgreSQL repository feature has been demonstrated successfully.");
        System.Console.WriteLine("The container and all demonstration data will now be removed.");
    }

    private static void ShowMetadata()
    {
        Section("ENTITY METADATA");

        var tableName = PostgresEntityExtensions.GetTableName<ExampleEntity>();
        var sensitiveProperty = typeof(ExampleEntity).GetProperty(nameof(ExampleEntity.SecretNote));
        var hasSensitiveMarker = sensitiveProperty?.IsDefined(typeof(SensitiveDataAttribute), false) == true;

        Check(tableName == "feature_examples", "TableNameAttribute resolves to feature_examples");
        Check(hasSensitiveMarker, "SensitiveDataAttribute is discoverable without exposing its value");
        System.Console.WriteLine("SensitiveDataAttribute is metadata; applications remain responsible for protection.");
    }

    private static async Task DemonstrateTableLifecycleAsync(PostgresRepository<ExampleEntity> repository)
    {
        Section("TABLE LIFECYCLE");

        await repository.EnsureTableAsync();
        Check(await repository.TableExistsAsync(), "EnsureTableAsync creates the mapped table when needed");
        Check(await repository.TableExistsAsync("feature_examples"), "TableExistsAsync(name) finds the table in the current schema");
        Check(await repository.TableExistsAsync("feature_examples", "public"), "TableExistsAsync(name, schema) supports explicit schemas");

        await repository.TruncateTableAsync(restartIdentity: true, cascade: false);
        Check(await repository.CountAsync(_ => true) == 0, "TruncateTableAsync resets the demonstration table");
    }

    private static async Task DemonstrateIndexesAsync(PostgresRepository<ExampleEntity> repository)
    {
        Section("INDEX MANAGEMENT");

        await repository.CreateIndexAsync(entity => entity.TestField);
        Check(true, "Created a simple ascending index");

        await repository.CreateIndexAsync(entity => entity.ExpiresAtUtc, TimeSpan.FromDays(7));
        Check(true, "TTL overload created a standard PostgreSQL index (expiry requires a cleanup job)");

        Expression<Func<ExampleEntity, object>>[] compoundFields =
        [
            entity => entity.ExternalId,
            entity => entity.Category
        ];

        await repository.CreateIndexAsync(
            compoundFields,
            filter: entity => entity.Category == "Greeting",
            unique: true);
        Check(true, "Created a compound unique index; the current partial-filter argument is reserved");

        (Expression<Func<ExampleEntity, object>> PropertyExpression, SortDirection Direction)[] directionalFields =
        [
            (entity => entity.Category, SortDirection.Ascending),
            (entity => entity.Score, SortDirection.Descending)
        ];

        await repository.CreateIndexAsync(directionalFields);
        Check(true, "Created a compound index with per-column sort directions");
    }

    private static async Task DemonstrateCreateAndReadAsync(
        PostgresRepository<ExampleEntity> repository,
        IReadOnlyList<ExampleEntity> entities)
    {
        Section("CREATE");

        await repository.InsertAsync(entities[0]);
        await repository.InsertAsync(entities.Skip(1).ToList());

        Check(entities.All(entity => entity.CreatedDateTime != default), "Single and bulk insert populate audit timestamps");
        Check(await repository.CountAsync(_ => true) == entities.Count, "CountAsync reports every inserted row");

        Section("READ, FILTER, SORT, AND PROJECTION");

        var byId = await repository.GetByIdAsync(entities[0].Id);
        Check(byId?.ExternalId == entities[0].ExternalId, "GetByIdAsync retrieves one row");

        var first = await repository.GetFirstOrDefaultAsync(
            predicate: entity => entity.Category == "Greeting",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);
        Check(first?.ExternalId == "postgres-002", "GetFirstOrDefaultAsync applies filtering and ordering");

        var single = await repository.GetSingleOrDefaultAsync(
            entity => entity.ExternalId == "postgres-003");
        Check(single?.TestField == "Planner", "GetSingleOrDefaultAsync returns the unique match");

        var sorted = await repository.GetAllAsync(
            predicate: entity => entity.Category == "Greeting",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);
        Check(sorted.Count == 3 && sorted[0].Score >= sorted[^1].Score, "GetAllAsync filters and sorts active rows");

        var summaries = await repository.GetAllAsync(
            projection: entity => new
            {
                entity.ExternalId,
                entity.TestField
            },
            predicate: entity => entity.Category == "Greeting");
        Check(summaries.Count == 3, "Projection returns only the requested columns");

        Check(await repository.ExistsAsync(entity => entity.ExternalId == "postgres-004"), "ExistsAsync finds an active row");

        Section("PAGINATION");

        var page = await repository.GetPagedListAsync(
            pageIndex: 1,
            pageSize: 2,
            predicate: entity => entity.Category == "Greeting",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);
        Check(page.Items.Count == 2 && page.TotalCount == 3 && page.TotalPages == 2, "Single-field pagination returns complete metadata");
        Check(!page.HasPreviousPage && page.HasNextPage, "Pagination navigation flags are correct");

        var ordering = new[]
        {
            new SortExpression<ExampleEntity>
            {
                Expression = entity => entity.Category,
                SortDirection = SortDirection.Ascending
            },
            new SortExpression<ExampleEntity>
            {
                Expression = entity => entity.Score,
                SortDirection = SortDirection.Descending
            }
        };

        var multiSortedPage = await repository.GetPagedListAsync(
            pageIndex: 1,
            pageSize: 4,
            predicate: null,
            orderBy: ordering);
        Check(multiSortedPage.Items.Count == 4, "Multi-field pagination accepts SortExpression values");
    }

    private static async Task DemonstrateUpdatesAndPullsAsync(
        PostgresRepository<ExampleEntity> repository,
        IReadOnlyList<ExampleEntity> entities)
    {
        Section("UPDATE");

        var primary = entities[0];
        primary.TestField = "Analytical Engine";
        await repository.UpdateAsync(primary);

        var updatedPrimary = await repository.GetByIdAsync(primary.Id);
        Check(updatedPrimary?.TestField == "Analytical Engine" && updatedPrimary.UpdatedDateTime != null, "UpdateAsync updates one row and its audit timestamp");

        var bulkTargets = entities.Skip(1).Take(2).ToList();
        await repository.UpdateManyAsync(bulkTargets, new Dictionary<string, object>
        {
            [nameof(ExampleEntity.Category)] = "UpdatedBatch"
        });

        var bulkUpdated = await repository.GetAllAsync(
            predicate: entity => entity.Category == "UpdatedBatch");
        Check(bulkUpdated.Count == 2 && bulkUpdated.All(entity => entity.UpdatedDateTime != null), "Transactional UpdateManyAsync updates rows and in-memory entities");

        Section("COLLECTION PULL OPERATION");

        await repository.PullAsync(
            field: entity => entity.Tags,
            fieldFilter: tag => tag == "temporary",
            documentPredicate: entity => entity.Category == "UpdatedBatch");

        var afterPull = await repository.GetAllAsync(
            predicate: entity => entity.Category == "UpdatedBatch");
        Check(afterPull.All(entity => !entity.Tags.Contains("temporary")), "PullAsync removes collection values through EF Core read/modify/write");
    }

    private static async Task DemonstrateDeletesAndRestoreAsync(
        PostgresRepository<ExampleEntity> repository,
        IReadOnlyList<ExampleEntity> entities)
    {
        Section("SOFT DELETE AND RESTORE");

        await repository.DeleteByIdAsync(entities[0].Id);
        Check(await repository.GetByIdAsync(entities[0].Id) is null, "DeleteByIdAsync soft-deletes and normal reads exclude the row");

        var deletedById = await repository.GetAllAsync(
            predicate: entity => entity.Id == entities[0].Id,
            includeDeletes: true);
        Check(deletedById.Single().IsDeleted, "includeDeletes exposes a soft-deleted row");

        await repository.RestoreAsync(entities[0].Id);
        Check(await repository.GetByIdAsync(entities[0].Id) is not null, "RestoreAsync(string) restores a row");

        await repository.DeleteAsync(entities[1]);
        await repository.RestoreAsync(entities[1]);
        Check(await repository.ExistsAsync(entity => entity.Id == entities[1].Id), "DeleteAsync and RestoreAsync(entity) complete the entity workflow");

        await repository.DeleteOneAsync(entity => entity.ExternalId == "postgres-004");
        Check(!await repository.ExistsAsync(entity => entity.ExternalId == "postgres-004"), "DeleteOneAsync soft-deletes the first predicate match");
        await repository.DeleteOneAsync(entity => entity.ExternalId == "postgres-004", hardDelete: true);

        var deletedMany = await repository.DeleteManyAsync(
            entity => entity.ExternalId.StartsWith("postgres-delete-many"));
        Check(deletedMany.Count == 2 && deletedMany.All(entity => entity.IsDeleted), "DeleteManyAsync(predicate) transactionally soft-deletes matching rows");

        foreach (var entity in deletedMany)
        {
            await repository.RestoreAsync(entity);
        }

        await repository.DeleteManyAsync(deletedMany, hardDelete: true);
        Check(await repository.CountAsync(entity => entity.ExternalId.StartsWith("postgres-delete-many")) == 0, "DeleteManyAsync(collection) permanently removes selected rows");

        Section("HARD DELETE");

        await repository.DeleteAsync(entities[0], hardDelete: true);
        await repository.DeleteByIdAsync(entities[2].Id, hardDelete: true);
        Check(await repository.CountAsync(entity => entity.Id == entities[0].Id || entity.Id == entities[2].Id) == 0, "DeleteAsync and DeleteByIdAsync support permanent deletion");
    }

    private static async Task RemoveIndexesAsync(PostgresRepository<ExampleEntity> repository)
    {
        Section("INDEX CLEANUP");

        Expression<Func<ExampleEntity, object>>[] compoundFields =
        [
            entity => entity.ExternalId,
            entity => entity.Category
        ];
        Expression<Func<ExampleEntity, object>>[] directionalFields =
        [
            entity => entity.Category,
            entity => entity.Score
        ];

        await repository.RemoveIndexAsync(compoundFields, unique: true);
        await repository.RemoveIndexAsync(entity => entity.TestField);
        await repository.RemoveIndexAsync(entity => entity.ExpiresAtUtc);
        await repository.RemoveIndexAsync(directionalFields);

        Check(true, "Removed compound, simple, TTL-overload, and directional indexes");
    }

    private static List<ExampleEntity> CreateEntities()
    {
        var expiry = DateTime.UtcNow.AddDays(7);

        return
        [
            NewEntity("postgres-001", "Ada", "Greeting", 80, ["demo", "legacy"], expiry),
            NewEntity("postgres-002", "Grace", "Greeting", 95, ["demo", "temporary"], expiry),
            NewEntity("postgres-003", "Planner", "Greeting", 90, ["database", "temporary"], expiry),
            NewEntity("postgres-004", "Delete one", "Cleanup", 40, ["cleanup"], expiry),
            NewEntity("postgres-delete-many-001", "Delete many A", "Cleanup", 30, ["cleanup"], expiry),
            NewEntity("postgres-delete-many-002", "Delete many B", "Cleanup", 20, ["cleanup"], expiry)
        ];
    }

    private static ExampleEntity NewEntity(
        string externalId,
        string testField,
        string category,
        int score,
        List<string> tags,
        DateTime expiry)
    {
        return new ExampleEntity
        {
            ExternalId = externalId,
            TestField = testField,
            Category = category,
            Score = score,
            Tags = tags,
            ExpiresAtUtc = expiry,
            SecretNote = "This value is deliberately never printed."
        };
    }

    private static void Banner(string title)
    {
        System.Console.WriteLine(new string('=', 72));
        System.Console.WriteLine(title);
        System.Console.WriteLine(new string('=', 72));
    }

    private static void Section(string title)
    {
        System.Console.WriteLine();
        System.Console.WriteLine($"--- {title} ---");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Demonstration check failed: {description}");
        }

        System.Console.WriteLine($"[OK] {description}");
    }
}
