using System.Reflection;

namespace Mongo.Helpers;

/// <summary>
/// Mongo Entity Extensions
/// </summary>
public static class MongoEntityExtensions
{
    /// <summary>
    /// Gets the collection name for an entity.
    /// </summary>
    /// <typeparam name="T">Mongo entity type</typeparam>
    /// <returns>Collection name</returns>
    public static string GetCollectionName<T>() where T : class, IMongoEntity
    {
        var type = typeof(T);
        var attribute = type.GetCustomAttribute<CollectionNameAttribute>(inherit: true);

        var rawName = attribute?.Name ?? type.Name;
        var lowerName = rawName.ToLowerInvariant();
        var collectionName = lowerName.Replace("entity", string.Empty);

        return collectionName;
    }
}