using System.Linq.Expressions;
using Mongo.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace Mongo.Tests;

[TestFixture]
public class MongoRepositoryTests
{
    private Mock<IMongoClient> _mockClient = null!;
    private Mock<IMongoDatabase> _mockDatabase = null!;
    private Mock<IMongoCollection<TestEntityForRepository>> _mockCollection = null!;
    private MongoRepository<TestEntityForRepository> _repository = null!;

    [SetUp]
    public void Setup()
    {
        _mockClient = new Mock<IMongoClient>();
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockCollection = new Mock<IMongoCollection<TestEntityForRepository>>();

        _mockClient.Setup(c => c.GetDatabase(It.IsAny<string>(), It.IsAny<MongoDatabaseSettings>()))
            .Returns(_mockDatabase.Object);
        _mockDatabase.Setup(d => d.GetCollection<TestEntityForRepository>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
            .Returns(_mockCollection.Object);

        _repository = new MongoRepository<TestEntityForRepository>(_mockClient.Object, "testdb");
    }

    [Test]
    public void Constructor_WithConnectionString_InitializesCorrectly()
    {
        var repo = new MongoRepository<TestEntityForRepository>("mongodb://localhost:27017", "testdb");
        
        Assert.That(repo, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithClient_InitializesCorrectly()
    {
        var repo = new MongoRepository<TestEntityForRepository>(_mockClient.Object, "testdb");
        
        Assert.That(repo, Is.Not.Null);
    }

    [Test]
    public async Task InsertAsync_SetsCreatedDateTime()
    {
        var entity = new TestEntityForRepository { Name = "Test" };
        _mockCollection.Setup(c => c.InsertOneAsync(
            It.IsAny<TestEntityForRepository>(),
            It.IsAny<InsertOneOptions>(),
            default))
            .Returns(Task.CompletedTask);

        await _repository.InsertAsync(entity);

        Assert.That(entity.CreatedDateTime, Is.Not.EqualTo(default(DateTime)));
        _mockCollection.Verify(c => c.InsertOneAsync(
            It.Is<TestEntityForRepository>(e => e.CreatedDateTime != default),
            It.IsAny<InsertOneOptions>(),
            default), Times.Once);
    }

    [Test]
    public async Task InsertAsync_WithCollection_SetsCreatedDateTimeForAll()
    {
        var entities = new List<TestEntityForRepository>
        {
            new TestEntityForRepository { Name = "Test1" },
            new TestEntityForRepository { Name = "Test2" }
        };
        _mockCollection.Setup(c => c.InsertManyAsync(
            It.IsAny<IEnumerable<TestEntityForRepository>>(),
            It.IsAny<InsertManyOptions>(),
            default))
            .Returns(Task.CompletedTask);

        await _repository.InsertAsync(entities);

        Assert.That(entities.All(e => e.CreatedDateTime != default(DateTime)), Is.True);
    }

    [Test]
    public async Task InsertAsync_WithNullCollection_DoesNotThrow()
    {
        await _repository.InsertAsync((ICollection<TestEntityForRepository>?)null);
        
        _mockCollection.Verify(c => c.InsertManyAsync(
            It.IsAny<IEnumerable<TestEntityForRepository>>(),
            It.IsAny<InsertManyOptions>(),
            default), Times.Never);
    }

    [Test]
    public async Task UpdateAsync_SetsUpdatedDateTime()
    {
        var entity = new TestEntityForRepository { Id = ObjectId.GenerateNewId().ToString(), Name = "Test" };
        _mockCollection.Setup(c => c.FindOneAndReplaceAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<TestEntityForRepository>(),
            It.IsAny<FindOneAndReplaceOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default))
            .ReturnsAsync(entity);

        await _repository.UpdateAsync(entity);

        Assert.That(entity.UpdatedDateTime, Is.Not.Null);
        _mockCollection.Verify(c => c.FindOneAndReplaceAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.Is<TestEntityForRepository>(e => e.UpdatedDateTime != null),
            It.IsAny<FindOneAndReplaceOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default), Times.Once);
    }

    [Test]
    public async Task DeleteByIdAsync_CallsDeleteOneAsync()
    {
        var id = ObjectId.GenerateNewId().ToString();
        _mockCollection.Setup(c => c.FindOneAndUpdateAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<UpdateDefinition<TestEntityForRepository>>(),
            It.IsAny<FindOneAndUpdateOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default))
            .ReturnsAsync((TestEntityForRepository)null!);

        await _repository.DeleteByIdAsync(id);

        _mockCollection.Verify(c => c.FindOneAndUpdateAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<UpdateDefinition<TestEntityForRepository>>(),
            It.IsAny<FindOneAndUpdateOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default), Times.Once);
    }

    [Test]
    public async Task DeleteByIdAsync_WithHardDelete_CallsDeleteOne()
    {
        var id = ObjectId.GenerateNewId().ToString();
        _mockCollection.Setup(c => c.FindOneAndDeleteAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<FindOneAndDeleteOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default))
            .ReturnsAsync((TestEntityForRepository)null!);

        await _repository.DeleteByIdAsync(id, hardDelete: true);

        _mockCollection.Verify(c => c.FindOneAndDeleteAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<FindOneAndDeleteOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default), Times.Once);
    }

    [Test]
    public async Task RestoreAsync_ResetsDeletedDateTime()
    {
        var id = ObjectId.GenerateNewId().ToString();
        var entity = new TestEntityForRepository { Id = id };
        _mockCollection.Setup(c => c.FindOneAndUpdateAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<UpdateDefinition<TestEntityForRepository>>(),
            It.IsAny<FindOneAndUpdateOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default))
            .ReturnsAsync(entity);

        await _repository.RestoreAsync(id);

        _mockCollection.Verify(c => c.FindOneAndUpdateAsync(
            It.IsAny<FilterDefinition<TestEntityForRepository>>(),
            It.IsAny<UpdateDefinition<TestEntityForRepository>>(),
            It.IsAny<FindOneAndUpdateOptions<TestEntityForRepository, TestEntityForRepository>>(),
            default), Times.Once);
    }

    [Test]
    public async Task CountAsync_ThrowsArgumentNullException_WhenPredicateIsNull()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.CountAsync(null!));
    }

    [Test]
    public async Task ExistsAsync_ThrowsArgumentNullException_WhenPredicateIsNull()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.ExistsAsync(null!));
    }

    [Test]
    public async Task CreateIndexAsync_WithEmptyFields_ThrowsArgumentException()
    {
        var emptyFields = new List<Expression<Func<TestEntityForRepository, object>>>();
        
        Assert.ThrowsAsync<ArgumentException>(async () => 
            await _repository.CreateIndexAsync(emptyFields));
    }

    [Test]
    public async Task RemoveIndexAsync_WithEmptyFields_ThrowsArgumentException()
    {
        var emptyFields = new List<Expression<Func<TestEntityForRepository, object>>>();
        
        Assert.ThrowsAsync<ArgumentException>(async () => 
            await _repository.RemoveIndexAsync(emptyFields));
    }

    [Test]
    public async Task RemoveIndexAsync_CompoundIndexMetadataWithLowercaseFields_DropsMatchingIndex()
    {
        const string indexName = "Name_1__createdDateTime_1";
        var indexMetadata = new BsonDocument
        {
            { "v", 2 },
            { "name", indexName },
            { "key", new BsonDocument { { "Name", 1 }, { "_createdDateTime", 1 } } },
            { "unique", true },
            { "partialFilterExpression", new BsonDocument("Name", "active") },
            { "ns", "testdb.testentities" }
        };
        var cursor = new Mock<IAsyncCursor<BsonDocument>>();
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        cursor.SetupGet(value => value.Current).Returns([indexMetadata]);
        var indexManager = new Mock<IMongoIndexManager<TestEntityForRepository>>();
        indexManager.Setup(value => value.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);
        _mockCollection.SetupGet(value => value.DocumentSerializer)
            .Returns(MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<TestEntityForRepository>());
        _mockCollection.SetupGet(value => value.Indexes).Returns(indexManager.Object);

        await _repository.RemoveIndexAsync(
            new Expression<Func<TestEntityForRepository, object>>[]
            {
                entity => entity.Name,
                entity => entity.CreatedDateTime
            },
            entity => entity.Name == "active",
            unique: true);

        indexManager.Verify(
            value => value.DropOneAsync(indexName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GetPagedListAsync_WithInvalidPageIndex_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => 
            await _repository.GetPagedListAsync(0, 10));
    }
}

[CollectionName("testentities")]
public class TestEntityForRepository : MongoEntity
{
    public string Name { get; set; } = string.Empty;
}

