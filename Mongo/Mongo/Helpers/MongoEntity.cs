using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Mongo.Helpers;

/// <summary>
/// Provides the shared identifier, audit, and soft-delete fields for MongoDB entities.
/// </summary>
public class MongoEntity : IMongoEntity
{
    /// <inheritdoc/>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id", Order = 1)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <inheritdoc/>
    [BsonElement("_createdDateTime", Order = 3)]
    [BsonRepresentation(BsonType.String)]
    public DateTime CreatedDateTime { get; set; }

    /// <inheritdoc/>
    [BsonElement("_updatedDateTime", Order = 4)]
    [BsonRepresentation(BsonType.String)]
    public DateTime? UpdatedDateTime { get; set; }

    /// <inheritdoc/>
    [BsonElement("_deletedDateTime", Order = 5)]
    public DateTime? DeletedDateTime { get; set; }

    /// <inheritdoc/>
    [BsonIgnore]
    public bool IsDeleted => DeletedDateTime != null;
}

/// <summary>
/// Interface: Mongo Entity
/// </summary>
public interface IMongoEntity
{
    /// <summary>
    /// Gets or sets the Unique Identifier
    /// </summary>
    string Id { get; set; }

    /// <summary>
    /// Gets or sets the Created Date
    /// </summary>
    DateTime CreatedDateTime { get; set; }

    /// <summary>
    /// Gets or sets the Updated Date
    /// </summary>
    DateTime? UpdatedDateTime { get; set; }

    /// <summary>
    /// Gets or sets the IsDeleted value
    /// </summary>
    bool IsDeleted { get; }

    /// <summary>
    /// Gets or sets the Deleted Date
    /// </summary>
    DateTime? DeletedDateTime { get; set; }
}
