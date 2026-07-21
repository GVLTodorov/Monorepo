using System;

namespace Mongo.Helpers;

/// <summary>
/// Sets the Collection Name for a Mongo Entity
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CollectionNameAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the MongoDB collection name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The MongoDB collection name.</param>
    public CollectionNameAttribute(string name)
    {
        Name = name;
    }
}
