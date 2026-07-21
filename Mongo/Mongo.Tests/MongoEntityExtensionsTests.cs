using Mongo.Helpers;

namespace Mongo.Tests;

[TestFixture]
public class MongoEntityExtensionsTests
{
    [Test]
    public void GetCollectionName_ReturnsLowercaseTypeName_WhenNoAttribute()
    {
        var collectionName = MongoEntityExtensions.GetCollectionName<TestEntityForExtensions>();
        
        // The method removes "entity" from anywhere in the name
        Assert.That(collectionName, Is.EqualTo("testforextensions"));
    }

    [Test]
    public void GetCollectionName_RemovesEntitySuffix_WhenTypeNameEndsWithEntity()
    {
        var collectionName = MongoEntityExtensions.GetCollectionName<TestEntityWithSuffixEntity>();
        
        // The method removes "entity" from anywhere in the name (removes both occurrences)
        Assert.That(collectionName, Is.EqualTo("testwithsuffix"));
    }

    [Test]
    public void GetCollectionName_ReturnsAttributeName_WhenAttributeExists()
    {
        var collectionName = MongoEntityExtensions.GetCollectionName<TestEntityWithAttribute>();
        
        Assert.That(collectionName, Is.EqualTo("customcollection"));
    }

    [Test]
    public void GetCollectionName_RemovesEntitySuffix_FromAttributeName()
    {
        var collectionName = MongoEntityExtensions.GetCollectionName<TestEntityWithAttributeEndingEntity>();
        
        Assert.That(collectionName, Is.EqualTo("custom"));
    }
}

// Test entities
public class TestEntityForExtensions : MongoEntity { }

[CollectionName("customcollection")]
public class TestEntityWithAttribute : MongoEntity { }

[CollectionName("customentity")]
public class TestEntityWithAttributeEndingEntity : MongoEntity { }

public class TestEntityWithSuffixEntity : MongoEntity { }

