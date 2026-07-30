using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Npgsql;
using Postgres.Helpers;
using SortDirection = Postgres.Helpers.SortDirection;
namespace Postgres;

/// <summary>Generic PostgreSQL repository implemented with Npgsql/ADO.NET.</summary>
public class PostgresRepository<T> : IPostgresRepository<T>, IAsyncDisposable
    where T : class, IPostgresEntity
{
    private readonly DbConnection _connection;
    private readonly bool _ownsConnection;
    private readonly PostgresRepositoryCore<T> _sql;

    /// <summary>Creates a repository backed by an Npgsql connection string.</summary>
    public PostgresRepository(string connectionString)
        : this(new NpgsqlConnection(ValidateConnectionString(connectionString)), ownsConnection: true)
    {
    }

    /// <summary>Creates a repository over an existing connection.</summary>
    public PostgresRepository(DbConnection connection)
        : this(connection, ownsConnection: false)
    {
    }

    private PostgresRepository(DbConnection connection, bool ownsConnection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = ownsConnection;
        var dialect = connection is NpgsqlConnection ? SqlDialect.PostgreSql : SqlDialect.Sqlite;
        _sql = new PostgresRepositoryCore<T>(
            connection,
            dialect,
            PostgresEntityExtensions.GetTableName<T>(),
            sql => OnCommandExecuting(sql));
    }

    /// <summary>True when commands use PostgreSQL SQL semantics.</summary>
    protected virtual bool UsesNpgsqlProvider => _connection is NpgsqlConnection;

    /// <summary>
    /// Called immediately before a SQL command is created. Derived repositories can use
    /// this non-EF hook for diagnostics and tests.
    /// </summary>
    protected virtual void OnCommandExecuting(string commandText)
    {
    }

    /// <inheritdoc cref="CreateIndexAsync(Expression{Func{T, object}})" />
    public Task CreateIndexAsync(Expression<Func<T, object>> field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return CreateIndexAsync([field]);
    }

    /// <inheritdoc cref="CreateIndexAsync(Expression{Func{T, object}}, TimeSpan)" />
    public Task CreateIndexAsync(Expression<Func<T, object>> field, TimeSpan expiresAfter)
    {
        ArgumentNullException.ThrowIfNull(field);

        return CreateIndexAsync(field);
    }

    /// <inheritdoc cref="CreateIndexAsync(IEnumerable{Expression{Func{T, object}}}, Expression{Func{T, bool}}, bool)" />
    public Task CreateIndexAsync(
        IEnumerable<Expression<Func<T, object>>> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var items = fields
            .Select(field => (_sql.GetMemberName(field), Descending: false))
            .ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException("Index must have at least one field.", nameof(fields));
        }

        return _sql.CreateIndexAsync(items, unique);
    }

    /// <inheritdoc cref="CreateIndexAsync(IEnumerable{ValueTuple{Expression{Func{T, object}}, SortDirection}}, Expression{Func{T, bool}}, bool)" />
    public Task CreateIndexAsync(
        IEnumerable<(Expression<Func<T, object>> PropertyExpression, SortDirection Direction)> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var items = fields
            .Select(field => (
                _sql.GetMemberName(field.PropertyExpression),
                Descending: field.Direction == SortDirection.Descending))
            .ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException("Index must have at least one field.", nameof(fields));
        }

        return _sql.CreateIndexAsync(items, unique);
    }

    /// <inheritdoc cref="RemoveIndexAsync(Expression{Func{T, object}})" />
    public Task RemoveIndexAsync(Expression<Func<T, object>> field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return _sql.RemoveIndexAsync([_sql.GetMemberName(field)]);
    }

    /// <inheritdoc cref="RemoveIndexAsync(IEnumerable{Expression{Func{T, object}}}, Expression{Func{T, bool}}, bool)" />
    public Task RemoveIndexAsync(
        IEnumerable<Expression<Func<T, object>>> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var names = fields.Select(_sql.GetMemberName).ToArray();
        if (names.Length == 0)
        {
            throw new ArgumentException("Index must have at least one field.", nameof(fields));
        }

        return _sql.RemoveIndexAsync(names);
    }

    /// <inheritdoc cref="GetAllAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    public Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false) =>
        _sql.GetAllAsync(
            predicate,
            orderBy is null ? null : _sql.GetMemberName(orderBy),
            sortDirection == SortDirection.Descending,
            includeDeletes);

    /// <inheritdoc cref="GetAllAsync{TProjection}(Expression{Func{T, TProjection}}, Expression{Func{T, bool}}, bool)" />
    public async Task<List<TProjection>> GetAllAsync<TProjection>(
        Expression<Func<T, TProjection>> projection,
        Expression<Func<T, bool>>? predicate = null,
        bool includeDeletes = false)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var entities = await _sql.GetAllAsync(
            predicate,
            orderBy: null,
            descending: false,
            includeDeletes);
        var projectedEntities = entities.Select(projection.Compile()).ToList();

        return projectedEntities;
    }

    /// <inheritdoc cref="GetPagedListAsync(int, int, Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    public Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex = 1,
        int pageSize = 50,
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false)
    {
        var ordering = new[]
        {
            (
                orderBy is null ? nameof(IPostgresOrSqliteEntity.CreatedDateTime) : _sql.GetMemberName(orderBy),
                sortDirection == SortDirection.Descending)
        };

        return GetPagedListCoreAsync(pageIndex, pageSize, predicate, ordering, includeDeletes);
    }

    /// <inheritdoc cref="GetPagedListAsync(int, int, Expression{Func{T, bool}}, SortExpression{T}[], bool)" />
    public Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        SortExpression<T>[] orderBy,
        bool includeDeletes = false)
    {
        var ordering = orderBy is { Length: > 0 }
            ? orderBy.Select(item => (
                _sql.GetMemberName(item.Expression),
                item.SortDirection == SortDirection.Descending)).ToArray()
            : [(nameof(IPostgresOrSqliteEntity.CreatedDateTime), false)];

        return GetPagedListCoreAsync(pageIndex, pageSize, predicate, ordering, includeDeletes);
    }

    private async Task<IPagedList<T>> GetPagedListCoreAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        IReadOnlyList<(string PropertyName, bool Descending)> ordering,
        bool includeDeletes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageIndex, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var countPredicate = predicate ?? (_ => true);
        var totalCount = await _sql.CountAsync(countPredicate, includeDeletes);
        var items = await _sql.GetAllAsync(
            predicate,
            ordering,
            includeDeletes,
            offset: (pageIndex - 1) * pageSize,
            limit: pageSize);
        var totalPages = totalCount == 0 ? 0 : (long)Math.Ceiling((double)totalCount / pageSize);
        var pagedList = new PagedList<T>(items, pageIndex, pageSize, totalPages, totalCount);

        return pagedList;
    }

    /// <inheritdoc cref="GetFirstOrDefaultAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    public async Task<T?> GetFirstOrDefaultAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeleted = false)
    {
        var results = await _sql.GetAllAsync(
            predicate,
            orderBy is null ? null : _sql.GetMemberName(orderBy),
            sortDirection == SortDirection.Descending,
            includeDeleted,
            limit: 1);
        var firstResult = results.FirstOrDefault();

        return firstResult;
    }

    /// <inheritdoc cref="GetSingleOrDefaultAsync(Expression{Func{T, bool}})" />
    public async Task<T?> GetSingleOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var results = await _sql.GetAllAsync(
            predicate,
            orderBy: null,
            descending: false,
            includeDeleted: true,
            limit: 2);

        return results.Count switch
        {
            0 => null,
            1 => results[0],
            _ => throw new InvalidOperationException("The query returned more than one element.")
        };
    }

    /// <inheritdoc cref="GetByIdAsync(string)" />
    public Task<T?> GetByIdAsync(string id) =>
        GetFirstOrDefaultAsync(entity => entity.Id == id);

    /// <inheritdoc cref="CountAsync(Expression{Func{T, bool}})" />
    public Task<long> CountAsync(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return _sql.CountAsync(predicate, includeDeleted: true);
    }

    /// <inheritdoc cref="ExistsAsync(Expression{Func{T, bool}})" />
    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return _sql.ExistsAsync(predicate, includeDeleted: false);
    }

    /// <inheritdoc cref="TableExistsAsync()" />
    public Task<bool> TableExistsAsync() => _sql.TableExistsAsync(_sql.TableName);

    /// <inheritdoc cref="TableExistsAsync(string, string)" />
    public Task<bool> TableExistsAsync(string tableName, string? schema = null) =>
        _sql.TableExistsAsync(tableName, schema);

    /// <inheritdoc cref="EnsureTableAsync()" />
    public Task EnsureTableAsync() => _sql.EnsureTableAsync();

    /// <inheritdoc cref="InsertAsync(T)" />
    public async Task InsertAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.CreatedDateTime = DateTime.UtcNow;
        await _sql.InsertAsync(entity);
    }

    /// <inheritdoc cref="InsertAsync(ICollection{T})" />
    public async Task InsertAsync(ICollection<T>? entities)
    {
        if (entities is null || entities.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var entity in entities)
        {
            entity.CreatedDateTime = now;
        }

        await _sql.InsertManyAsync(entities);
    }

    /// <inheritdoc cref="UpdateAsync(T)" />
    public async Task UpdateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.UpdatedDateTime = DateTime.UtcNow;
        await _sql.UpdateAsync(entity);
    }

    /// <inheritdoc cref="UpdateManyAsync(ICollection{T}, Dictionary{string, object})" />
    public async Task UpdateManyAsync(
        ICollection<T>? entities,
        Dictionary<string, object> updatedKeyValues)
    {
        ArgumentNullException.ThrowIfNull(updatedKeyValues);
        if (entities is null || entities.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var entity in entities)
        {
            entity.UpdatedDateTime = now;
            foreach (var (propertyName, value) in updatedKeyValues)
            {
                var property = _sql.TryGetProperty(propertyName);
                if (property is { CanWrite: true })
                {
                    property.SetValue(entity, value);
                }
            }
        }

        await _sql.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var entity in entities)
            {
                await _sql.UpdateAsync(entity, transaction);
            }
        });
    }

    /// <inheritdoc cref="PullAsync{TItem}(Expression{Func{T, IEnumerable{TItem}}}, Expression{Func{TItem, bool}}, Expression{Func{T, bool}}, bool)" />
    public async Task PullAsync<TItem>(
        Expression<Func<T, IEnumerable<TItem>>> field,
        Expression<Func<TItem, bool>>? fieldFilter = null,
        Expression<Func<T, bool>>? documentPredicate = null,
        bool includeDeleted = false)
    {
        ArgumentNullException.ThrowIfNull(field);
        var entities = await _sql.GetAllAsync(
            documentPredicate,
            orderBy: null,
            descending: false,
            includeDeleted);
        var getItems = field.Compile();
        var filter = fieldFilter?.Compile() ?? (_ => true);
        var member = StripConvert(field.Body) as MemberExpression;
        var property = member?.Expression == field.Parameters[0]
            ? member.Member as PropertyInfo
            : null;
        var now = DateTime.UtcNow;

        await _sql.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var entity in entities)
            {
                var retained = getItems(entity).Where(item => !filter(item)).ToList();
                if (property is { CanWrite: true } && property.PropertyType.IsAssignableFrom(retained.GetType()))
                {
                    property.SetValue(entity, retained);
                }

                entity.UpdatedDateTime = now;
                await _sql.UpdateAsync(entity, transaction);
            }
        });
    }

    /// <inheritdoc cref="DeleteByIdAsync(string, bool)" />
    public async Task DeleteByIdAsync(string id, bool hardDelete = false)
    {
        var entity = await GetSingleOrDefaultAsync(item => item.Id == id);
        if (entity is not null)
        {
            await DeleteAsync(entity, hardDelete);
        }
    }

    /// <inheritdoc cref="DeleteAsync(T, bool)" />
    public async Task DeleteAsync(T entity, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (hardDelete)
        {
            await _sql.DeleteByIdAsync(entity.Id);
        }
        else
        {
            entity.DeletedDateTime = DateTime.UtcNow;
            entity.UpdatedDateTime = entity.DeletedDateTime;
            await _sql.UpdateAsync(entity);
        }
    }

    /// <inheritdoc cref="DeleteOneAsync(Expression{Func{T, bool}}, bool)" />
    public async Task DeleteOneAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var entity = await GetFirstOrDefaultAsync(predicate, includeDeleted: true);
        if (entity is not null)
        {
            await DeleteAsync(entity, hardDelete);
        }
    }

    /// <inheritdoc cref="DeleteManyAsync(Expression{Func{T, bool}}, bool)" />
    public async Task<List<T>> DeleteManyAsync(
        Expression<Func<T, bool>> predicate,
        bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var entities = await _sql.GetAllAsync(
            predicate,
            orderBy: null,
            descending: false,
            includeDeleted: true);
        if (entities.Count == 0)
        {
            return [];
        }

        try
        {
            var now = DateTime.UtcNow;
            await _sql.ExecuteInTransactionAsync(async transaction =>
            {
                foreach (var entity in entities)
                {
                    if (hardDelete)
                    {
                        await _sql.DeleteByIdAsync(entity.Id, transaction);
                    }
                    else
                    {
                        entity.DeletedDateTime = now;
                        entity.UpdatedDateTime = now;
                        await _sql.UpdateAsync(entity, transaction);
                    }
                }
            });

            return entities;
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc cref="DeleteManyAsync(ICollection{T}, bool)" />
    public async Task DeleteManyAsync(ICollection<T> items, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        await _sql.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var entity in items)
            {
                if (hardDelete)
                {
                    await _sql.DeleteByIdAsync(entity.Id, transaction);
                }
                else
                {
                    entity.DeletedDateTime = now;
                    entity.UpdatedDateTime = now;
                    await _sql.UpdateAsync(entity, transaction);
                }
            }
        });
    }

    /// <inheritdoc cref="TruncateTableAsync(bool, bool)" />
    public Task TruncateTableAsync(bool restartIdentity = false, bool cascade = false) =>
        _sql.TruncateAsync(restartIdentity, cascade);

    /// <inheritdoc cref="RestoreAsync(string)" />
    public async Task RestoreAsync(string id)
    {
        var entity = await GetSingleOrDefaultAsync(item => item.Id == id && item.DeletedDateTime != null);
        if (entity is not null)
        {
            await RestoreAsync(entity);
        }
    }

    /// <inheritdoc cref="RestoreAsync(T)" />
    public Task RestoreAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.DeletedDateTime = null;

        return UpdateAsync(entity);
    }

    /// <inheritdoc cref="DisposeAsync()" />
    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection)
        {
            await _connection.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private static string ValidateConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return connectionString;
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    // This private shape provides a provider-neutral nameof target while both public
    // entity interfaces retain their existing namespaces.
    private interface IPostgresOrSqliteEntity
    {
        DateTime CreatedDateTime { get; }
    }
}

/// <summary>Marker interface for PostgreSQL repositories.</summary>
public interface IPostgresRepository
{
}

/// <summary>Defines the PostgreSQL repository contract for an entity type.</summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IPostgresRepository<T> : IPostgresRepository where T : class, IPostgresEntity
{
    /// <inheritdoc cref="CreateIndexAsync(Expression{Func{T, object}})" />
    Task CreateIndexAsync(Expression<Func<T, object>> field);

    /// <inheritdoc cref="CreateIndexAsync(Expression{Func{T, object}}, TimeSpan)" />
    Task CreateIndexAsync(Expression<Func<T, object>> field, TimeSpan expiresAfter);

    /// <inheritdoc cref="CreateIndexAsync(IEnumerable{Expression{Func{T, object}}}, Expression{Func{T, bool}}, bool)" />
    Task CreateIndexAsync(
        IEnumerable<Expression<Func<T, object>>> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false);

    /// <inheritdoc cref="CreateIndexAsync(IEnumerable{ValueTuple{Expression{Func{T, object}}, SortDirection}}, Expression{Func{T, bool}}, bool)" />
    Task CreateIndexAsync(
        IEnumerable<(Expression<Func<T, object>> PropertyExpression, SortDirection Direction)> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false);

    /// <inheritdoc cref="RemoveIndexAsync(Expression{Func{T, object}})" />
    Task RemoveIndexAsync(Expression<Func<T, object>> field);

    /// <inheritdoc cref="RemoveIndexAsync(IEnumerable{Expression{Func{T, object}}}, Expression{Func{T, bool}}, bool)" />
    Task RemoveIndexAsync(
        IEnumerable<Expression<Func<T, object>>> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false);

    /// <inheritdoc cref="InsertAsync(T)" />
    Task InsertAsync(T entity);

    /// <inheritdoc cref="InsertAsync(ICollection{T})" />
    Task InsertAsync(ICollection<T> entities);

    /// <inheritdoc cref="UpdateAsync(T)" />
    Task UpdateAsync(T entity);

    /// <inheritdoc cref="UpdateManyAsync(ICollection{T}, Dictionary{string, object})" />
    Task UpdateManyAsync(ICollection<T>? entities, Dictionary<string, object> updatedKeyValues);

    /// <inheritdoc cref="PullAsync{TItem}(Expression{Func{T, IEnumerable{TItem}}}, Expression{Func{TItem, bool}}, Expression{Func{T, bool}}, bool)" />
    Task PullAsync<TItem>(
        Expression<Func<T, IEnumerable<TItem>>> field,
        Expression<Func<TItem, bool>>? fieldFilter = null,
        Expression<Func<T, bool>>? documentPredicate = null,
        bool includeDeleted = false);

    /// <inheritdoc cref="DeleteByIdAsync(string, bool)" />
    Task DeleteByIdAsync(string id, bool hardDelete = false);

    /// <inheritdoc cref="DeleteAsync(T, bool)" />
    Task DeleteAsync(T entity, bool hardDelete = false);

    /// <inheritdoc cref="DeleteOneAsync(Expression{Func{T, bool}}, bool)" />
    Task DeleteOneAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false);

    /// <inheritdoc cref="DeleteManyAsync(Expression{Func{T, bool}}, bool)" />
    Task<List<T>> DeleteManyAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false);

    /// <inheritdoc cref="DeleteManyAsync(ICollection{T}, bool)" />
    Task DeleteManyAsync(ICollection<T> items, bool hardDelete = false);

    /// <inheritdoc cref="TruncateTableAsync(bool, bool)" />
    Task TruncateTableAsync(bool restartIdentity = false, bool cascade = false);

    /// <inheritdoc cref="RestoreAsync(T)" />
    Task RestoreAsync(T entity);

    /// <inheritdoc cref="RestoreAsync(string)" />
    Task RestoreAsync(string id);

    /// <inheritdoc cref="CountAsync(Expression{Func{T, bool}})" />
    Task<long> CountAsync(Expression<Func<T, bool>> predicate);

    /// <inheritdoc cref="ExistsAsync(Expression{Func{T, bool}})" />
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);

    /// <inheritdoc cref="TableExistsAsync()" />
    Task<bool> TableExistsAsync();

    /// <inheritdoc cref="TableExistsAsync(string, string)" />
    Task<bool> TableExistsAsync(string tableName, string? schema = null);

    /// <inheritdoc cref="EnsureTableAsync()" />
    Task EnsureTableAsync();

    /// <inheritdoc cref="GetByIdAsync(string)" />
    Task<T?> GetByIdAsync(string id);

    /// <inheritdoc cref="GetAllAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false);

    /// <inheritdoc cref="GetAllAsync{TProjection}(Expression{Func{T, TProjection}}, Expression{Func{T, bool}}, bool)" />
    Task<List<TProjection>> GetAllAsync<TProjection>(
        Expression<Func<T, TProjection>> projection,
        Expression<Func<T, bool>>? predicate = null,
        bool includeDeletes = false);

    /// <inheritdoc cref="GetPagedListAsync(int, int, Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex = 1,
        int pageSize = 50,
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false);

    /// <inheritdoc cref="GetPagedListAsync(int, int, Expression{Func{T, bool}}, SortExpression{T}[], bool)" />
    Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        SortExpression<T>[] orderBy,
        bool includeDeletes = false);

    /// <inheritdoc cref="GetFirstOrDefaultAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    Task<T?> GetFirstOrDefaultAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeleted = false);

    /// <inheritdoc cref="GetSingleOrDefaultAsync(Expression{Func{T, bool}})" />
    Task<T?> GetSingleOrDefaultAsync(Expression<Func<T, bool>> predicate);
}
