using System.Linq.Expressions;
using Mongo.Helpers;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using SortDirection = Mongo.Helpers.SortDirection;

namespace Mongo.Console;

public static class Program
{
    private const string DatabaseName = "repository_demo";

    public static async Task Main()
    {
        Banner("Mongo repository feature demonstration");
        System.Console.WriteLine("Starting an ephemeral MongoDB replica set with Testcontainers...");

        await using var mongo = new MongoDbBuilder("mongo:6.0")
            .WithReplicaSet("rs0")
            .Build();

        await mongo.StartAsync();

        var repository = new MongoRepository<ExampleEntity>(
            mongo.GetConnectionString(),
            DatabaseName);

        ShowMetadata();
        await CreateIndexesAsync(repository);

        var entities = CreateEntities();
        await CreateAndReadAsync(repository, entities);
        await UpdateAndPullAsync(repository, entities);
        await DeleteAndRestoreAsync(repository, entities);
        await RemoveIndexesAsync(repository);

        Section("COMPLETE");
        System.Console.WriteLine("Every public Mongo repository feature has been demonstrated successfully.");
        System.Console.WriteLine("The container and all demonstration data will now be removed.");
    }

    private static void ShowMetadata()
    {
        Section("ENTITY METADATA");

        var collectionName = MongoEntityExtensions.GetCollectionName<ExampleEntity>();
        var sensitiveProperty = typeof(ExampleEntity).GetProperty(nameof(ExampleEntity.SecretNote));
        var hasSensitiveMarker = sensitiveProperty?.IsDefined(typeof(SensitiveDataAttribute), false) == true;

        Check(collectionName == "feature_examples", "CollectionNameAttribute resolves to feature_examples");
        Check(hasSensitiveMarker, "SensitiveDataAttribute is discoverable without exposing its value");
        System.Console.WriteLine("SensitiveDataAttribute is metadata; applications remain responsible for protection.");
    }

    private static async Task CreateIndexesAsync(MongoRepository<ExampleEntity> repository)
    {
        Section("INDEX MANAGEMENT");

        await repository.CreateIndexAsync(entity => entity.TestField);
        Check(true, "Created a simple ascending index");

        await repository.CreateIndexAsync(entity => entity.ExpiresAtUtc, TimeSpan.Zero);
        Check(true, "Created a native TTL index on a BSON date field");

        Expression<Func<ExampleEntity, object>>[] partialUniqueFields =
        [
            entity => entity.ExternalId,
            entity => entity.Category
        ];

        await repository.CreateIndexAsync(
            partialUniqueFields,
            filter: entity => entity.Category == "Greeting",
            unique: true);
        Check(true, "Created a compound, unique, partial index");

        (Expression<Func<ExampleEntity, object>> PropertyExpression, SortDirection Direction)[] directionalFields =
        [
            (entity => entity.Category, SortDirection.Ascending),
            (entity => entity.Score, SortDirection.Descending)
        ];

        await repository.CreateIndexAsync(directionalFields);
        Check(true, "Created a compound index with per-field sort directions");
    }

    private static async Task CreateAndReadAsync(
        MongoRepository<ExampleEntity> repository,
        List<ExampleEntity> entities)
    {
        Section("CREATE");

        await repository.InsertAsync(entities[0]);
        await repository.InsertAsync([.. entities.Skip(1)]);

        Check(entities.All(entity => entity.CreatedDateTime != default), "Single and bulk insert populate audit timestamps");
        Check(await repository.CountAsync(_ => true) == entities.Count, "CountAsync reports every inserted document");

        Section("READ, FILTER, SORT, AND PROJECTION");

        var byId = await repository.GetByIdAsync(entities[0].Id);
        Check(byId?.ExternalId == entities[0].ExternalId, "GetByIdAsync retrieves one document");

        var first = await repository.GetFirstOrDefaultAsync(
            predicate: entity => entity.Category == "Greeting",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);
        Check(first?.ExternalId == "mongo-002", "GetFirstOrDefaultAsync applies filtering and ordering");

        var single = await repository.GetSingleOrDefaultAsync(
            entity => entity.ExternalId == "mongo-003");
        Check(single?.TestField == "Compiler", "GetSingleOrDefaultAsync returns the unique match");

        var sorted = await repository.GetAllAsync(
            predicate: entity => entity.Category == "Greeting",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);
        Check(sorted.Count == 3 && sorted[0].Score >= sorted[^1].Score, "GetAllAsync filters and sorts active documents");

        var projection = Builders<ExampleEntity>.Projection.Expression(entity => new
        {
            entity.ExternalId,
            entity.TestField
        });
        var summaries = await repository.GetAllAsync(
            projection,
            predicate: entity => entity.Category == "Greeting");
        Check(summaries.Count == 3, "Projection returns only the requested fields");

        Check(await repository.ExistsAsync(entity => entity.ExternalId == "mongo-004"), "ExistsAsync finds an active document");

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

    private static async Task UpdateAndPullAsync(
        MongoRepository<ExampleEntity> repository,
        List<ExampleEntity> entities)
    {
        Section("UPDATE");

        var primary = entities[0];
        primary.TestField = "Analytical Engine";
        await repository.UpdateAsync(primary);

        var updatedPrimary = await repository.GetByIdAsync(primary.Id);
        Check(updatedPrimary?.TestField == "Analytical Engine" && updatedPrimary.UpdatedDateTime != null, "UpdateAsync replaces one document and updates its audit timestamp");

        var bulkTargets = entities.Skip(1).Take(2).ToList();
        await repository.UpdateManyAsync(bulkTargets, new Dictionary<string, object>
        {
            [nameof(ExampleEntity.Category)] = "UpdatedBatch"
        });

        var bulkUpdated = await repository.GetAllAsync(entity => entity.Category == "UpdatedBatch");
        Check(bulkUpdated.Count == 2 && bulkUpdated.All(entity => entity.UpdatedDateTime != null), "Transactional UpdateManyAsync updates documents and in-memory entities");

        Section("COLLECTION PULL OPERATIONS");

        await repository.PullAsync(
            field: new StringFieldDefinition<ExampleEntity>(nameof(ExampleEntity.Tags)),
            value: "legacy",
            entitiesToBeUpdated: [primary],
            fieldExistsFilter: entity => entity.Tags);

        var afterValuePull = await repository.GetByIdAsync(primary.Id);
        Check(afterValuePull is not null && !afterValuePull.Tags.Contains("legacy"), "FieldDefinition PullAsync removes a specific value from selected documents");

        await repository.PullAsync(
            field: entity => entity.Tags,
            fieldFilter: tag => tag == "temporary",
            documentPredicate: entity => entity.Category == "UpdatedBatch");

        var afterPredicatePull = await repository.GetAllAsync(entity => entity.Category == "UpdatedBatch");
        Check(afterPredicatePull.All(entity => !entity.Tags.Contains("temporary")), "Typed PullAsync removes values matching a predicate");
    }

    private static async Task DeleteAndRestoreAsync(
        MongoRepository<ExampleEntity> repository,
        List<ExampleEntity> entities)
    {
        Section("SOFT DELETE AND RESTORE");

        await repository.DeleteByIdAsync(entities[0].Id);
        Check(await repository.GetByIdAsync(entities[0].Id) is null, "DeleteByIdAsync soft-deletes and normal reads exclude the document");

        var deletedById = await repository.GetAllAsync(
            predicate: entity => entity.Id == entities[0].Id,
            includeDeletes: true);
        Check(deletedById.Single().IsDeleted, "includeDeletes exposes a soft-deleted document");

        await repository.RestoreAsync(entities[0].Id);
        Check(await repository.GetByIdAsync(entities[0].Id) is not null, "RestoreAsync(string) restores a document");

        await repository.DeleteAsync(entities[1]);
        await repository.RestoreAsync(entities[1]);
        Check(await repository.ExistsAsync(entity => entity.Id == entities[1].Id), "DeleteAsync and RestoreAsync(entity) complete the entity workflow");

        await repository.DeleteOneAsync(entity => entity.ExternalId == "mongo-004");
        Check(!await repository.ExistsAsync(entity => entity.ExternalId == "mongo-004"), "DeleteOneAsync soft-deletes the first predicate match");
        await repository.DeleteOneAsync(entity => entity.ExternalId == "mongo-004", hardDelete: true);

        var deletedMany = await repository.DeleteManyAsync(
            entity => entity.ExternalId.StartsWith("mongo-delete-many"));
        Check(deletedMany.Count == 2 && deletedMany.All(entity => entity.IsDeleted), "DeleteManyAsync(predicate) transactionally soft-deletes matching documents");

        foreach (var entity in deletedMany)
        {
            await repository.RestoreAsync(entity);
        }

        await repository.DeleteManyAsync(deletedMany, hardDelete: true);
        Check(await repository.CountAsync(entity => entity.ExternalId.StartsWith("mongo-delete-many")) == 0, "DeleteManyAsync(collection) permanently removes selected documents");

        Section("HARD DELETE");

        await repository.DeleteAsync(entities[0], hardDelete: true);
        await repository.DeleteByIdAsync(entities[2].Id, hardDelete: true);
        Check(await repository.CountAsync(entity => entity.Id == entities[0].Id || entity.Id == entities[2].Id) == 0, "DeleteAsync and DeleteByIdAsync support permanent deletion");
    }

    private static async Task RemoveIndexesAsync(MongoRepository<ExampleEntity> repository)
    {
        Section("INDEX CLEANUP");

        Expression<Func<ExampleEntity, object>>[] partialUniqueFields =
        [
            entity => entity.ExternalId,
            entity => entity.Category
        ];

        await repository.RemoveIndexAsync(
            partialUniqueFields,
            filter: entity => entity.Category == "Greeting",
            unique: true);
        await repository.RemoveIndexAsync(entity => entity.TestField);
        await repository.RemoveIndexAsync(entity => entity.ExpiresAtUtc);
        await repository.RemoveIndexAsync(entity => entity.Category);

        Check(true, "Removed compound, simple, TTL, and directional indexes");
    }

    private static List<ExampleEntity> CreateEntities()
    {
        var expiry = DateTime.UtcNow.AddDays(7);

        return
        [
            NewEntity("mongo-001", "Ada", "Greeting", 80, ["demo", "legacy"], expiry),
            NewEntity("mongo-002", "Grace", "Greeting", 95, ["demo", "temporary"], expiry),
            NewEntity("mongo-003", "Compiler", "Greeting", 90, ["database", "temporary"], expiry),
            NewEntity("mongo-004", "Delete one", "Cleanup", 40, ["cleanup"], expiry),
            NewEntity("mongo-delete-many-001", "Delete many A", "Cleanup", 30, ["cleanup"], expiry),
            NewEntity("mongo-delete-many-002", "Delete many B", "Cleanup", 20, ["cleanup"], expiry)
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
        var exampleEntity = new ExampleEntity
        {
            ExternalId = externalId,
            TestField = testField,
            Category = category,
            Score = score,
            Tags = tags,
            ExpiresAtUtc = expiry,
            SecretNote = "This value is deliberately never printed."
        };

        return exampleEntity;
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
