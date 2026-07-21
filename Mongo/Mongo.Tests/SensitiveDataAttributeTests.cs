using Mongo.Helpers;

namespace Mongo.Tests;

[TestFixture]
public class SensitiveDataAttributeTests
{
    [Test]
    public void SensitiveDataAttribute_CanBeAppliedToProperty()
    {
        var property = typeof(TestEntityForSensitiveData).GetProperty(nameof(TestEntityForSensitiveData.SensitiveField));
        var attributes = property?.GetCustomAttributes(typeof(SensitiveDataAttribute), true);
        
        Assert.That(attributes, Is.Not.Null);
        Assert.That(attributes, Is.Not.Empty);
    }

    [Test]
    public void SensitiveDataAttribute_CanBeInstantiated()
    {
        var attribute = new SensitiveDataAttribute();
        
        Assert.That(attribute, Is.Not.Null);
    }
}

public class TestEntityForSensitiveData
{
    [SensitiveData]
    public string SensitiveField { get; set; } = string.Empty;
}

