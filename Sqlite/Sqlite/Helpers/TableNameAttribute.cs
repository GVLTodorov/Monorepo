using System;

namespace Sqlite.Helpers;

/// <summary>
/// Sets the Table Name for a Sqlite Entity (equivalent to Mongo's CollectionName)
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class TableNameAttribute : Attribute
{
    /// <summary>Gets the table name supplied for the entity.</summary>
    public string Name { get; }

    /// <summary>Creates a table-name attribute.</summary>
    /// <param name="name">The SQLite table name.</param>
    public TableNameAttribute(string name)
    {
        Name = name;
    }
}
