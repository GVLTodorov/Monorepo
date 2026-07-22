using System;

namespace Sqlite.Helpers;

/// <summary>
/// Interface: Sqlite Entity (base for all repository entities)
/// </summary>
public interface ISqliteEntity
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

/// <summary>
/// Base entity for SQLite tables with audit and soft-delete support
/// </summary>
public class SqliteEntity : ISqliteEntity
{
    /// <inheritdoc/>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public DateTime CreatedDateTime { get; set; }

    /// <inheritdoc/>
    public DateTime? UpdatedDateTime { get; set; }

    /// <inheritdoc/>
    public DateTime? DeletedDateTime { get; set; }

    /// <inheritdoc/>
    public bool IsDeleted => DeletedDateTime != null;
}
