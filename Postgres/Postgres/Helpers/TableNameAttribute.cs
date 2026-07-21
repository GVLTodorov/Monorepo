using System;

namespace Postgres.Helpers;

/// <summary>
/// Sets the Table Name for a Postgres Entity (equivalent to Mongo's CollectionName)
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class TableNameAttribute : Attribute
{
    /// <summary>Gets the table name supplied for the entity.</summary>
    public string Name { get; }

    /// <summary>Creates a table-name attribute.</summary>
    /// <param name="name">The PostgreSQL table name.</param>
    public TableNameAttribute(string name)
    {
        Name = name;
    }
}
