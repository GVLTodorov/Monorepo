using Mongo.Helpers;

namespace Mongo.Tests;

[TestFixture]
public class CollectionNameAttributeTests
{
    [Test]
    public void CollectionNameAttribute_Initializes_WithName()
    {
        var attribute = new CollectionNameAttribute("testcollection");
        
        Assert.That(attribute.Name, Is.EqualTo("testcollection"));
    }

    [Test]
    public void CollectionNameAttribute_CanBeAppliedToClass()
    {
        var attribute = typeof(TestEntityForAttribute).GetCustomAttributes(typeof(CollectionNameAttribute), true);
        
        Assert.That(attribute, Is.Not.Empty);
        Assert.That(((CollectionNameAttribute)attribute[0]).Name, Is.EqualTo("test"));
    }
}

[CollectionName("test")]
public class TestEntityForAttribute : MongoEntity { }

