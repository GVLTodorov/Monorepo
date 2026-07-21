using System.Reflection;

namespace Postgres.Helpers;

/// <summary>
/// Postgres Entity Extensions
/// </summary>
public static class PostgresEntityExtensions
{
    /// <summary>
    /// Gets the table name for an entity.
    /// </summary>
    /// <typeparam name="T">Postgres entity type</typeparam>
    /// <returns>Table name (snake_case by convention for Postgres)</returns>
    public static string GetTableName<T>() where T : class, IPostgresEntity
    {
        var type = typeof(T);
        var attribute = type.GetCustomAttribute<TableNameAttribute>(inherit: true);

        var rawName = attribute?.Name ?? type.Name;
        var tableName = rawName.EndsWith("Entity", StringComparison.OrdinalIgnoreCase)
            ? rawName[..^"Entity".Length]
            : rawName;

        return ToSnakeCase(tableName);
    }

    /// <summary>
    /// Converts a string to snake_case.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }
}
