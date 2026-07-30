using System.Linq.Expressions;
using System.Reflection;
using Mongo.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using SortDirection = Mongo.Helpers.SortDirection;

namespace Mongo.Tests;

[CollectionName("coverage_entities")]
public sealed class CoverageMongoEntity : MongoEntity
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public List<string> Tags { get; set; } = [];
}

[TestFixture]
public sealed class MongoRepositoryCoverageTests
{
    private Mock<IMongoClient> _client = null!;
    private Mock<IMongoDatabase> _database = null!;
    private Mock<IMongoCollection<CoverageMongoEntity>> _collection = null!;
    private Mock<IMongoIndexManager<CoverageMongoEntity>> _indexes = null!;
    private Mock<IClientSessionHandle> _session = null!;
    private MongoRepository<CoverageMongoEntity> _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IMongoClient>();
        _database = new Mock<IMongoDatabase>();
        _collection = new Mock<IMongoCollection<CoverageMongoEntity>>();
        _indexes = new Mock<IMongoIndexManager<CoverageMongoEntity>>();
        _session = new Mock<IClientSessionHandle>();

        _client
            .Setup(client => client.GetDatabase(
                It.IsAny<string>(),
                It.IsAny<MongoDatabaseSettings>()))
            .Returns(_database.Object);
        _client
            .Setup(client => client.StartSessionAsync(
                It.IsAny<ClientSessionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session.Object);
        _database
            .Setup(database => database.GetCollection<CoverageMongoEntity>(
                It.IsAny<string>(),
                It.IsAny<MongoCollectionSettings>()))
            .Returns(_collection.Object);
        _collection.SetupGet(collection => collection.Indexes).Returns(_indexes.Object);
        _collection.SetupGet(collection => collection.DocumentSerializer)
            .Returns(BsonSerializer.LookupSerializer<CoverageMongoEntity>());

        _repository = new MongoRepository<CoverageMongoEntity>(_client.Object, "TEST_DATABASE");
    }

    [Test]
    public async Task CreateIndexOverloads_BuildExpectedModels()
    {
        var models = new List<CreateIndexModel<CoverageMongoEntity>>();
        _indexes
            .Setup(indexes => indexes.CreateOneAsync(
                It.IsAny<CreateIndexModel<CoverageMongoEntity>>(),
                It.IsAny<CreateOneIndexOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateIndexModel<CoverageMongoEntity>, CreateOneIndexOptions, CancellationToken>(
                (model, _, _) => models.Add(model))
            .ReturnsAsync("index");

        await _repository.CreateIndexAsync(entity => entity.Name);
        await _repository.CreateIndexAsync(entity => entity.Name, TimeSpan.FromMinutes(5));
        await _repository.CreateIndexAsync(
            new Expression<Func<CoverageMongoEntity, object>>[]
            {
                entity => entity.Name,
                entity => entity.Score
            },
            entity => entity.Score > 0,
            unique: true);
        await _repository.CreateIndexAsync(
            new[]
            {
                ((Expression<Func<CoverageMongoEntity, object>>)(entity => entity.Name), SortDirection.Ascending),
                ((Expression<Func<CoverageMongoEntity, object>>)(entity => entity.Score), SortDirection.Descending)
            },
            entity => entity.Score > 0,
            unique: true);
        await _repository.CreateIndexAsync(
            new[]
            {
                ((Expression<Func<CoverageMongoEntity, object>>)(entity => entity.Name), SortDirection.Descending),
                ((Expression<Func<CoverageMongoEntity, object>>)(entity => entity.Score), SortDirection.Ascending)
            });

        Assert.Multiple(() =>
        {
            Assert.That(models, Has.Count.EqualTo(5));
            Assert.That(models[1].Options.ExpireAfter, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(models[2].Options.Unique, Is.True);
            Assert.That(models[2].Options.PartialFilterExpression, Is.Not.Null);
            Assert.That(models[3].Options.Unique, Is.True);
            Assert.That(models[3].Options.PartialFilterExpression, Is.Not.Null);
            Assert.That(
                async () => await _repository.CreateIndexAsync(
                    Array.Empty<(Expression<Func<CoverageMongoEntity, object>>, SortDirection)>()),
                Throws.ArgumentException);
        });
    }

    [Test]
    public async Task RemoveSingleIndex_CoversEmptyMissingAndMatchingMetadata()
    {
        _indexes.SetupSequence(indexes => indexes.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor<BsonDocument>())
            .ReturnsAsync(CreateCursor(new BsonDocument
            {
                ["name"] = "other_1",
                ["key"] = new BsonDocument("Other", 1)
            }))
            .ReturnsAsync(CreateCursor(new BsonDocument
            {
                ["name"] = "Name_1",
                ["key"] = new BsonDocument("Name", 1)
            }));
        _indexes
            .Setup(indexes => indexes.DropOneAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _repository.RemoveIndexAsync(entity => entity.Name);
        await _repository.RemoveIndexAsync(entity => entity.Name);
        await _repository.RemoveIndexAsync(entity => entity.Name);

        _indexes.Verify(
            indexes => indexes.DropOneAsync("Name_1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CompoundIndexRemoval_WhenMetadataDoesNotMatch_DoesNotDrop()
    {
        _indexes
            .Setup(indexes => indexes.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(new BsonDocument
            {
                ["v"] = 2,
                ["name"] = "different",
                ["key"] = new BsonDocument("Score", -1)
            }));

        await _repository.RemoveIndexAsync(
            new Expression<Func<CoverageMongoEntity, object>>[] { entity => entity.Name },
            unique: true);

        _indexes.Verify(
            indexes => indexes.DropOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task QueryProjectionSinglePagingCountAndExistsPaths_ReturnExpectedData()
    {
        var alpha = Create(ObjectId.GenerateNewId().ToString(), "Alpha", 2);
        var beta = Create(ObjectId.GenerateNewId().ToString(), "Beta", 1);
        _collection
            .SetupSequence(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOptions<CoverageMongoEntity, CoverageMongoEntity>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCursor(alpha, beta))
            .ReturnsAsync(CreateCursor(alpha, beta))
            .ReturnsAsync(CreateCursor(alpha))
            .ReturnsAsync(CreateCursor(alpha, beta));
        _collection
            .Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOptions<CoverageMongoEntity, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateCursor("Alpha", "Beta"));
        _collection
            .Setup(collection => collection.CountDocumentsAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        SetupAggregateResult(alpha);

        var ascending = await _repository.GetAllAsync(
            includeDeletes: true,
            orderBy: entity => entity.Score);
        var descending = await _repository.GetAllAsync(
            entity => entity.Score > 0,
            entity => entity.Score,
            SortDirection.Descending);
        var projected = await _repository.GetAllAsync(
            Builders<CoverageMongoEntity>.Projection.Expression(entity => entity.Name),
            includeDeletes: true);
        var single = await _repository.GetSingleOrDefaultAsync(entity => entity.Id == alpha.Id);
        var byId = await _repository.GetByIdAsync(alpha.Id);
        var first = await _repository.GetFirstOrDefaultAsync(
            entity => entity.Score > 0,
            entity => entity.Name,
            SortDirection.Descending,
            includeDeleted: true);
        var page = await _repository.GetPagedListAsync(
            1,
            1,
            entity => entity.Score > 0,
            [
                new SortExpression<CoverageMongoEntity>
                {
                    Expression = entity => entity.Name,
                    SortDirection = SortDirection.Ascending
                },
                new SortExpression<CoverageMongoEntity>
                {
                    Expression = entity => entity.Score,
                    SortDirection = SortDirection.Descending
                }
            ],
            includeDeletes: true);
        var count = await _repository.CountAsync(entity => entity.Score > 0);
        var exists = await _repository.ExistsAsync(entity => entity.Id == alpha.Id);

        Assert.Multiple(() =>
        {
            Assert.That(ascending.Select(entity => entity.Id), Is.EqualTo(new[] { beta.Id, alpha.Id }));
            Assert.That(descending.Select(entity => entity.Id), Is.EqualTo(new[] { alpha.Id, beta.Id }));
            Assert.That(projected, Is.EqualTo(new[] { "Alpha", "Beta" }));
            Assert.That(single?.Id, Is.EqualTo(alpha.Id));
            Assert.That(byId?.Id, Is.EqualTo(alpha.Id));
            Assert.That(first?.Id, Is.EqualTo(alpha.Id));
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(page.TotalPages, Is.EqualTo(2));
            Assert.That(count, Is.EqualTo(2));
            Assert.That(exists, Is.True);
        });
    }

    [Test]
    public async Task PagingWithNoMatches_UsesZeroTotalPagesAndDefaultSort()
    {
        SetupFindResults();
        _collection
            .Setup(collection => collection.CountDocumentsAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var page = await _repository.GetPagedListAsync(
            1,
            10,
            predicate: null,
            orderBy: Array.Empty<SortExpression<CoverageMongoEntity>>(),
            includeDeletes: false);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(page.TotalCount, Is.Zero);
            Assert.That(page.TotalPages, Is.Zero);
        });
    }

    [Test]
    public async Task UpdateMany_CoversEmptySuccessMismatchAndExceptionTransactions()
    {
        var entities = new[]
        {
            Create("a", "Alpha", 1),
            Create("b", "Beta", 2)
        };

        await _repository.UpdateManyAsync([], new Dictionary<string, object>());
        _client.Verify(
            client => client.StartSessionAsync(
                It.IsAny<ClientSessionOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        await _repository.UpdateManyAsync(null, new Dictionary<string, object>());

        _collection
            .Setup(collection => collection.UpdateManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult.Acknowledged(2, 2, null));

        await _repository.UpdateManyAsync(
            entities,
            new Dictionary<string, object>
            {
                [nameof(CoverageMongoEntity.Name)] = "updated",
                ["Missing"] = "ignored"
            });

        Assert.Multiple(() =>
        {
            Assert.That(entities.Select(entity => entity.Name), Is.All.EqualTo("updated"));
            Assert.That(entities.Select(entity => entity.UpdatedDateTime), Is.All.Not.Null);
        });
        _session.Verify(session => session.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);

        _collection
            .Setup(collection => collection.UpdateManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult.Acknowledged(2, 1, null));
        await _repository.UpdateManyAsync(entities, new Dictionary<string, object>());

        _collection
            .Setup(collection => collection.UpdateManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write"));
        await _repository.UpdateManyAsync(entities, new Dictionary<string, object>());

        _session.Verify(session => session.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Test]
    public async Task PullOverloads_CoverOptionalFiltersAndDriverFailure()
    {
        _collection
            .Setup(collection => collection.UpdateManyAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        var entities = new[] { Create("a", "Alpha", 1) };

        await _repository.PullAsync(
            new StringFieldDefinition<CoverageMongoEntity>("Tags"),
            "remove");
        await _repository.PullAsync(
            new StringFieldDefinition<CoverageMongoEntity>("Tags"),
            "remove",
            entities,
            entity => entity.Tags);
        await _repository.PullAsync(
            entity => entity.Tags,
            fieldFilter: null,
            documentPredicate: null,
            includeDeleted: false);
        await _repository.PullAsync(
            entity => entity.Tags,
            tag => tag == "remove",
            entity => entity.Score > 0,
            includeDeleted: true);

        _collection
            .Setup(collection => collection.UpdateManyAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("write"));

        Assert.DoesNotThrowAsync(async () =>
            await _repository.PullAsync(
                new StringFieldDefinition<CoverageMongoEntity>("Tags"),
                "remove"));
    }

    [Test]
    public async Task RestoreAndDeleteOne_CoverFoundAndMissingEntities()
    {
        var entity = Create("a", "Alpha", 1);
        _collection
            .SetupSequence(collection => collection.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOneAndUpdateOptions<CoverageMongoEntity, CoverageMongoEntity>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity)
            .ReturnsAsync((CoverageMongoEntity)null!)
            .ReturnsAsync(entity)
            .ReturnsAsync(entity)
            .ReturnsAsync((CoverageMongoEntity)null!)
            .ReturnsAsync(entity);
        _collection
            .SetupSequence(collection => collection.FindOneAndDeleteAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOneAndDeleteOptions<CoverageMongoEntity, CoverageMongoEntity>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity)
            .ReturnsAsync((CoverageMongoEntity)null!)
            .ReturnsAsync(entity);

        await _repository.RestoreAsync("a");
        await _repository.RestoreAsync("missing");
        await _repository.RestoreAsync(entity);
        await _repository.DeleteOneAsync(item => item.Id == "a");
        await _repository.DeleteOneAsync(item => item.Id == "missing");
        await _repository.DeleteOneAsync(item => item.Id == "a", hardDelete: true);
        await _repository.DeleteOneAsync(item => item.Id == "missing", hardDelete: true);
        await _repository.DeleteAsync(entity);
        await _repository.DeleteAsync(entity, hardDelete: true);

        _collection.Verify(
            collection => collection.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOneAndUpdateOptions<CoverageMongoEntity, CoverageMongoEntity>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(6));
        _collection.Verify(
            collection => collection.FindOneAndDeleteAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOneAndDeleteOptions<CoverageMongoEntity, CoverageMongoEntity>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Test]
    public async Task DeleteMany_CoversSoftAndHardSuccessMismatchAndFailure()
    {
        var entities = new[]
        {
            Create("a", "Alpha", 1),
            Create("b", "Beta", 2)
        };
        SetupFindResults(entities);
        _collection
            .Setup(collection => collection.UpdateManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult.Acknowledged(2, 2, null));
        _collection
            .Setup(collection => collection.DeleteManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<DeleteOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteResult.Acknowledged(2));

        var softDeleted = await _repository.DeleteManyAsync(entity => entity.Score > 0);
        var hardDeleted = await _repository.DeleteManyAsync(entity => entity.Score > 0, hardDelete: true);
        await _repository.DeleteManyAsync(entities, hardDelete: true);

        Assert.Multiple(() =>
        {
            Assert.That(softDeleted, Has.Count.EqualTo(2));
            Assert.That(softDeleted, Has.All.Property(nameof(MongoEntity.DeletedDateTime)).Not.Null);
            Assert.That(hardDeleted, Has.Count.EqualTo(2));
        });

        _collection
            .Setup(collection => collection.UpdateManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult.Acknowledged(2, 1, null));
        _collection
            .Setup(collection => collection.DeleteManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<DeleteOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteResult.Acknowledged(1));

        Assert.That(await _repository.DeleteManyAsync(entity => true), Is.Empty);
        Assert.That(await _repository.DeleteManyAsync(entity => true, hardDelete: true), Is.Empty);

        _collection
            .Setup(collection => collection.UpdateManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateDefinition<CoverageMongoEntity>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("soft"));
        _collection
            .Setup(collection => collection.DeleteManyAsync(
                _session.Object,
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<DeleteOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hard"));

        Assert.That(await _repository.DeleteManyAsync(entity => true), Is.Empty);
        Assert.That(await _repository.DeleteManyAsync(entity => true, hardDelete: true), Is.Empty);
        _session.Verify(session => session.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Test]
    public void PrivateBuilders_RenderBothSidesOfOptionalBranches()
    {
        var nullPredicate = InvokePrivate(
            "BuildPredicateAsFilterDefinition",
            [typeof(Expression<Func<CoverageMongoEntity, bool>>)],
            [null]);
        var predicate = InvokePrivate(
            "BuildPredicateAsFilterDefinition",
            [typeof(Expression<Func<CoverageMongoEntity, bool>>)],
            [(Expression<Func<CoverageMongoEntity, bool>>)(entity => entity.Score > 2)]);
        var defaultExpression = (Expression<Func<CoverageMongoEntity, bool>>)InvokePrivate(
            "BuildPredicateOrDefault",
            [typeof(Expression<Func<CoverageMongoEntity, bool>>)],
            [null])!;
        var suppliedExpression = (Expression<Func<CoverageMongoEntity, bool>>)InvokePrivate(
            "BuildPredicateOrDefault",
            [typeof(Expression<Func<CoverageMongoEntity, bool>>)],
            [(Expression<Func<CoverageMongoEntity, bool>>)(entity => entity.Score > 2)])!;
        var defaultOrder = (Expression<Func<CoverageMongoEntity, object>>)InvokePrivate(
            "BuildOrderByOrDefault",
            [typeof(Expression<Func<CoverageMongoEntity, object>>)],
            [null])!;
        var suppliedOrder = (Expression<Func<CoverageMongoEntity, object>>)InvokePrivate(
            "BuildOrderByOrDefault",
            [typeof(Expression<Func<CoverageMongoEntity, object>>)],
            [(Expression<Func<CoverageMongoEntity, object>>)(entity => entity.Name)])!;
        var emptySort = InvokePrivate(
            "BuildOrderByAsFilterDefinition",
            [typeof(SortExpression<CoverageMongoEntity>[])],
            [Array.Empty<SortExpression<CoverageMongoEntity>>()]);
        var combinedSort = InvokePrivate(
            "BuildOrderByAsFilterDefinition",
            [typeof(SortExpression<CoverageMongoEntity>[])],
            [
                new[]
                {
                    new SortExpression<CoverageMongoEntity>
                    {
                        Expression = entity => entity.Name,
                        SortDirection = SortDirection.Ascending
                    },
                    new SortExpression<CoverageMongoEntity>
                    {
                        Expression = entity => entity.Score,
                        SortDirection = SortDirection.Descending
                    }
                }
            ]);
        var excludedDeletes = (Expression<Func<CoverageMongoEntity, bool>>)InvokePrivate(
            "BuildSoftDeleteExpression",
            [typeof(bool)],
            [false])!;
        var includedDeletes = (Expression<Func<CoverageMongoEntity, bool>>)InvokePrivate(
            "BuildSoftDeleteExpression",
            [typeof(bool)],
            [true])!;

        Assert.Multiple(() =>
        {
            Assert.That(nullPredicate, Is.Not.Null);
            Assert.That(predicate, Is.Not.Null);
            Assert.That(defaultExpression.Compile()(Create("a", "A", 0)), Is.True);
            Assert.That(suppliedExpression.Compile()(Create("a", "A", 0)), Is.False);
            Assert.That(defaultOrder.Body.ToString(), Does.Contain(nameof(MongoEntity.CreatedDateTime)));
            Assert.That(suppliedOrder.Body.ToString(), Does.Contain(nameof(CoverageMongoEntity.Name)));
            Assert.That(emptySort, Is.Not.Null);
            Assert.That(combinedSort, Is.Not.Null);
            Assert.That(excludedDeletes.Compile()(Create("a", "A", 0)), Is.True);
            Assert.That(includedDeletes.Compile()(Create("a", "A", 0)), Is.True);
        });
    }

    private void SetupFindResults(params CoverageMongoEntity[] entities) =>
        _collection
            .Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<CoverageMongoEntity>>(),
                It.IsAny<FindOptions<CoverageMongoEntity, CoverageMongoEntity>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateCursor(entities));

    private void SetupAggregateResult(CoverageMongoEntity entity)
    {
        var result = new AggregateFacetResults(
        [
            new AggregateFacetResult<CoverageMongoEntity>("data", [entity])
        ]);
        _collection
            .Setup(collection => collection.AggregateAsync(
                It.IsAny<PipelineDefinition<CoverageMongoEntity, AggregateFacetResults>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateCursor(result));
    }

    private object? InvokePrivate(string name, Type[] parameterTypes, object?[] arguments) =>
        typeof(MongoRepository<CoverageMongoEntity>)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic, parameterTypes)!
            .Invoke(_repository, arguments);

    private static IAsyncCursor<T> CreateCursor<T>(params T[] values)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor
            .SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        cursor.SetupGet(value => value.Current).Returns(values);
        return cursor.Object;
    }

    private static CoverageMongoEntity Create(string id, string name, int score) => new()
    {
        Id = id,
        Name = name,
        Score = score,
        Tags = ["keep", "remove"]
    };
}
