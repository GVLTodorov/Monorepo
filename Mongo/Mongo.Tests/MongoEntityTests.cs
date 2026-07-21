using Mongo.Helpers;
using MongoDB.Bson;

namespace Mongo.Tests;

[TestFixture]
public class MongoEntityTests
{
    [Test]
    public void MongoEntity_Initializes_WithGeneratedId()
    {
        var entity = new MongoEntity();
        
        Assert.That(entity.Id, Is.Not.Null);
        Assert.That(entity.Id, Is.Not.Empty);
        Assert.That(ObjectId.TryParse(entity.Id, out _), Is.True);
    }

    [Test]
    public void MongoEntity_IsDeleted_ReturnsFalse_WhenDeletedDateTimeIsNull()
    {
        var entity = new MongoEntity
        {
            DeletedDateTime = null
        };
        
        Assert.That(entity.IsDeleted, Is.False);
    }

    [Test]
    public void MongoEntity_IsDeleted_ReturnsTrue_WhenDeletedDateTimeIsSet()
    {
        var entity = new MongoEntity
        {
            DeletedDateTime = DateTime.UtcNow
        };
        
        Assert.That(entity.IsDeleted, Is.True);
    }

    [Test]
    public void MongoEntity_Properties_CanBeSet()
    {
        var now = DateTime.UtcNow;
        var entity = new MongoEntity
        {
            CreatedDateTime = now,
            UpdatedDateTime = now,
            DeletedDateTime = now
        };
        
        Assert.That(entity.CreatedDateTime, Is.EqualTo(now));
        Assert.That(entity.UpdatedDateTime, Is.EqualTo(now));
        Assert.That(entity.DeletedDateTime, Is.EqualTo(now));
    }
}

