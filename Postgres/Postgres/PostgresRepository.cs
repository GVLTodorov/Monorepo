using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Postgres.Helpers;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using SortDirection = Postgres.Helpers.SortDirection;

namespace Postgres;

/// <summary>
/// Generic Repository for PostgreSQL using Entity Framework Core
/// </summary>
public class PostgresRepository<T> : IPostgresRepository<T> where T : class, IPostgresEntity
{
    private readonly PostgresDbContext<T> _context;
    private readonly string _tableName;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    /// <summary>Gets the EF Core entity set used by the repository.</summary>
    protected DbSet<T> DbSet => _context.Entities;

    private ILogger Logger { get; set; }

    /// <summary>
    /// Gets whether this repository is using the Npgsql provider. The virtual
    /// seam allows provider-specific orchestration to be tested without a live server.
    /// </summary>
    protected virtual bool UsesNpgsqlProvider =>
        _context.Database.ProviderName!.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Constructor accepting a connection string.
    /// The repository will create its own DbContext instance.
    /// </summary>
    /// <param name="connectionString"></param>
    public PostgresRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _context = new PostgresDbContext<T>(connectionString);
        _tableName = PostgresEntityExtensions.GetTableName<T>();
        Logger = new NullLoggerFactory().CreateLogger<PostgresRepository<T>>();
    }

    /// <summary>
    /// Constructor accepting an existing DbContext instance.
    /// This allows for better control over the context's lifecycle and configuration.
    /// </summary>
    /// <param name="context"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public PostgresRepository(PostgresDbContext<T> context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tableName = PostgresEntityExtensions.GetTableName<T>();
        Logger = new NullLoggerFactory().CreateLogger<PostgresRepository<T>>();
    }


    /// <inheritdoc/>
    public Task CreateIndexAsync(Expression<Func<T, object>> field)
    {
        return CreateIndexAsync([field], null, false);
    }

    /// <inheritdoc/>
    public Task CreateIndexAsync(Expression<Func<T, object>> field, TimeSpan expiresAfter)
    {
        Logger.LogDebug("PostgreSQL does not support TTL indexes; creating standard index.");
        
        return CreateIndexAsync(field);
    }

    /// <inheritdoc/>
    public async Task CreateIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        await EnsureTableAsync();

        var fieldList = fields.ToList();
        if (fieldList.Count == 0)
        {
            throw new ArgumentException("Index must have at least one field", nameof(fields));
        }

        var columns = 
            fieldList.Select(f => 
                QuoteColumn(ExpressionHelper.GetMemberName(f))).ToList();

        var idxName = $"idx_{_tableName}_{string.Join("_", columns).Replace("\"", "")}";
        var uniqueStr = unique ? " UNIQUE" : "";
        // Partial index (WHERE) would require translating expression to SQL; skip for simplicity
        var indexColumns = string.Join(", ", columns);
        var sql =
            $"CREATE{uniqueStr} INDEX IF NOT EXISTS {QuoteIdentifier(idxName)} "
            + $"ON {QuoteIdentifier(_tableName)} ({indexColumns});";

        await _context.Database.ExecuteSqlRawAsync(sql);

        Logger.LogDebug("Created index {IndexName} on {Table}", idxName, _tableName);
    }

    /// <inheritdoc/>
    public async Task CreateIndexAsync(
        IEnumerable<(Expression<Func<T, object>> PropertyExpression, SortDirection Direction)> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(fields);
        await EnsureTableAsync();

        var fieldList = fields.ToList();
        if (fieldList.Count == 0)
            throw new ArgumentException("Index must have at least one field", nameof(fields));

        var parts = fieldList.Select(f => QuoteColumn(ExpressionHelper.GetMemberName(f.PropertyExpression)) + (f.Direction == SortDirection.Descending ? " DESC" : " ASC"));
        var idxName = $"idx_{_tableName}_{string.Join("_", fieldList.Select(f => ExpressionHelper.GetMemberName(f.PropertyExpression)))}";
        var uniqueStr = unique ? " UNIQUE" : "";
        var indexColumns = string.Join(", ", parts);
        var sql =
            $"CREATE{uniqueStr} INDEX IF NOT EXISTS {QuoteIdentifier(idxName)} "
            + $"ON {QuoteIdentifier(_tableName)} ({indexColumns});";

        await _context.Database.ExecuteSqlRawAsync(sql);

        Logger.LogDebug("Created index {IndexName} on {Table}", idxName, _tableName);
    }

    /// <inheritdoc/>
    public async Task RemoveIndexAsync(Expression<Func<T, object>> field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var memberName = ExpressionHelper.GetMemberName(field);
        var idxName = $"idx_{_tableName}_{memberName}";

        var sql = $"DROP INDEX IF EXISTS {QuoteIdentifier(idxName)};";
        await _context.Database.ExecuteSqlRawAsync(sql);

    }

    /// <inheritdoc/>
    public async Task RemoveIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var fieldList = fields.ToList();
        if (fieldList.Count == 0)
            throw new ArgumentException("Index must have at least one field", nameof(fields));

        var idxName = $"idx_{_tableName}_{string.Join("_", fieldList.Select(f => ExpressionHelper.GetMemberName(f)))}";

        var sql = $"DROP INDEX IF EXISTS {QuoteIdentifier(idxName)};";
        await _context.Database.ExecuteSqlRawAsync(sql);

    }

    private static string QuoteColumn(string memberName) => QuoteIdentifier(memberName);

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";


    /// <inheritdoc/>
    public async Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false)
    {
        await EnsureTableAsync();

        var query = ApplySoftDelete(DbSet.AsQueryable(), includeDeletes);
        if (predicate != null)
            query = query.Where(predicate);

        var ordered = orderBy != null
            ? (sortDirection == SortDirection.Ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy))
            : query.OrderBy(e => e.CreatedDateTime);


        return await ordered.ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<List<TProjection>> GetAllAsync<TProjection>(
        Expression<Func<T, TProjection>> projection,
        Expression<Func<T, bool>>? predicate = null,
        bool includeDeletes = false)
    {
        ArgumentNullException.ThrowIfNull(projection);
        await EnsureTableAsync();

        var query = ApplySoftDelete(DbSet.AsQueryable(), includeDeletes);
        if (predicate != null)
            query = query.Where(predicate);


        return await query.Select(projection).ToListAsync();
    }

    /// <inheritdoc/>
    public Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex = 1,
        int pageSize = 50,
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false)
    {
        var paged =  GetPagedListAsync(pageIndex, pageSize, predicate,
            [new SortExpression<T>
            {
                Expression = orderBy ?? (e => e.CreatedDateTime), 
                SortDirection = sortDirection
            }],
            includeDeletes);


        return paged;
    }

    /// <inheritdoc/>
    public async Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        SortExpression<T>[] orderBy,
        bool includeDeletes = false)
    {
        if (pageIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        await EnsureTableAsync();

        var query = ApplySoftDelete(DbSet.AsQueryable(), includeDeletes);
        if (predicate != null)
            query = query.Where(predicate);

        var totalCount = await query.LongCountAsync();

        IOrderedQueryable<T>? ordered;
        if (orderBy is { Length: > 0 })
        {
            var first = orderBy[0];
            ordered = first.SortDirection == SortDirection.Ascending
                ? query.OrderBy(first.Expression)
                : query.OrderByDescending(first.Expression);
            for (var i = 1; i < orderBy.Length; i++)
            {
                var next = orderBy[i];
                ordered = next.SortDirection == SortDirection.Ascending
                    ? ordered.ThenBy(next.Expression)
                    : ordered.ThenByDescending(next.Expression);
            }
        }
        else
        {
            ordered = query.OrderBy(e => e.CreatedDateTime);
        }

        var items = await ordered
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalPages = totalCount > 0 ? (long)Math.Ceiling((double)totalCount / pageSize) : 0;

        return new PagedList<T>(items, pageIndex, pageSize, totalPages, totalCount);
    }

    /// <inheritdoc/>
    public async Task<T?> GetFirstOrDefaultAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeleted = false)
    {
        await EnsureTableAsync();

        var query = ApplySoftDelete(DbSet.AsQueryable(), includeDeleted);
        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        var ordered = orderBy != null
            ? (sortDirection == SortDirection.Ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy))
            : query.OrderBy(e => e.CreatedDateTime);


        var orderedFod = await ordered.FirstOrDefaultAsync();


        return orderedFod;
    }

    /// <inheritdoc/>
    public async Task<T?> GetSingleOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        await EnsureTableAsync();

        return await DbSet.Where(predicate).SingleOrDefaultAsync();
    }

    /// <inheritdoc/>
    public async Task<T?> GetByIdAsync(string id)
    {
        return await GetFirstOrDefaultAsync(p => p.Id == id);
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        await EnsureTableAsync();


        return await DbSet.Where(predicate).LongCountAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        await EnsureTableAsync();

        return await ApplySoftDelete(DbSet.AsQueryable(), includeDeleted: false).AnyAsync(predicate);
    }

    /// <inheritdoc/>
    public Task<bool> TableExistsAsync() => TableExistsAsync(_tableName, schema: null);

    /// <inheritdoc/>
    public async Task<bool> TableExistsAsync(string tableName, string? schema = null)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));

        var providerName = _context.Database.ProviderName;
        if (!UsesNpgsqlProvider
            && providerName!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return await TableExistsInSqliteAsync(tableName);
        }

        if (!UsesNpgsqlProvider)
        {
            return await _context.Database.CanConnectAsync();
        }

        var normalizedTable = tableName.ToLowerInvariant();
        var normalizedSchema = schema?.ToLowerInvariant();

        // FormattableString keeps parameters bound (no string concat); COALESCE uses current_schema() when schema is null.

        return await _context.Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_name = {normalizedTable}
                      AND table_schema = COALESCE({normalizedSchema}, current_schema()))
                    AS "Value"
                """)
            .SingleAsync();
    }

    private async Task<bool> TableExistsInSqliteAsync(string tableName)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND lower(name) = lower($name));";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt64(scalar) == 1;
    }

    /// <inheritdoc/>
    public async Task EnsureTableAsync()
    {
        if (_tableEnsured)
        {
            return;
        }

        await _ensureTableLock.WaitAsync();
        try
        {
            if (_tableEnsured)
            {
                return;
            }

            if (UsesNpgsqlProvider)
            {
                if (!await TableExistsAsync())
                {
                    var creator = _context.GetService<IRelationalDatabaseCreator>();
                    await creator.CreateTablesAsync();
                }
            }
            else
            {
                await _context.Database.EnsureCreatedAsync();
            }

            _tableEnsured = true;
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task InsertAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureTableAsync();

        entity.CreatedDateTime = DateTime.UtcNow;

        await DbSet.AddAsync(entity);

        await _context.SaveChangesAsync();
        
        Logger.LogDebug("Postgres Insert => Table[{Table}]: {Id}", _tableName, entity.Id);
    }

    /// <inheritdoc/>
    public async Task InsertAsync(ICollection<T>? entities)
    {
        if (entities == null || entities.Count == 0)
        {
            return;
        }

        await EnsureTableAsync();

        foreach (var entity in entities)
        {
            entity.CreatedDateTime = DateTime.UtcNow;
        }

        await DbSet.AddRangeAsync(entities);

        await _context.SaveChangesAsync();

        foreach (var entity in entities)
        {
            Logger.LogDebug("Postgres Insert => Table[{Table}]: {Id}", _tableName, entity.Id);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await EnsureTableAsync();

        entity.UpdatedDateTime = DateTime.UtcNow;
        DbSet.Update(entity);

        await _context.SaveChangesAsync();
        Logger.LogDebug("Postgres Update => Table[{Table}]: {Id}", _tableName, entity.Id);
    }

    /// <inheritdoc/>
    public async Task UpdateManyAsync(ICollection<T>? entities, Dictionary<string, object> updatedKeyValues)
    {
        ArgumentNullException.ThrowIfNull(updatedKeyValues);

        if (entities == null || entities.Count == 0)
        {
            return;
        }

        await EnsureTableAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var utcNow = DateTime.UtcNow;
            var type = typeof(T);
            var cachedProps = new Dictionary<string, PropertyInfo?>();

            foreach (var entity in entities)
            {
                entity.UpdatedDateTime = utcNow;
                foreach (var kv in updatedKeyValues)
                {
                    if (!cachedProps.TryGetValue(kv.Key, out var prop))
                    {
                        prop = type.GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                        cachedProps[kv.Key] = prop;
                    }
                    if (prop?.CanWrite == true)
                    {
                        prop.SetValue(entity, kv.Value);
                    }
                }
            }

            DbSet.UpdateRange(entities);

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            foreach (var entity in entities)
            {
                Logger.LogDebug("Postgres Update => Table[{Table}]: {Id}", _tableName, entity.Id);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing to Postgres: {Message}", ex.Message);

            await transaction.RollbackAsync();

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PullAsync<TItem>(
        Expression<Func<T, IEnumerable<TItem>>> field,
        Expression<Func<TItem, bool>>? fieldFilter = null,
        Expression<Func<T, bool>>? documentPredicate = null,
        bool includeDeleted = false)
    {
        await EnsureTableAsync();

        fieldFilter ??= _ => true;
        var query = ApplySoftDelete(DbSet.AsQueryable(), includeDeleted);
        if (documentPredicate != null)
            {
                query = query.Where(documentPredicate);
            }

        var list = await query.ToListAsync();
        var compiledFilter = fieldFilter.Compile();
        foreach (var doc in list)
        {
            var collection = field.Compile().Invoke(doc);
            var asList = collection.ToList();
            var removed = asList.Where(compiledFilter).ToList();
            foreach (var r in removed)
            {
                asList.Remove(r);
            }
            var memberExpr = field.Body is UnaryExpression u ? u.Operand as MemberExpression : field.Body as MemberExpression;
            if (memberExpr?.Member is PropertyInfo { CanWrite: true } pi)
            {
                pi.SetValue(doc, asList);
            }
        }

        foreach (var doc in list)
        {
            doc.UpdatedDateTime = DateTime.UtcNow;
        }

        DbSet.UpdateRange(list);

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteByIdAsync(string id, bool hardDelete = false)
    {
        await DeleteOneAsync(x => x.Id == id, hardDelete);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(T entity, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await DeleteOneAsync(x => x.Id == entity.Id, hardDelete);
    }

    /// <inheritdoc/>
    public async Task DeleteOneAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        await EnsureTableAsync();

        var q = DbSet.Where(predicate);
        if (!hardDelete)
        {
            q = q.Where(x => x.DeletedDateTime == null);
        }

        var entity = await q.FirstOrDefaultAsync();
        if (entity == null)
        {
            return;
        }

        if (hardDelete)
        {
            DbSet.Remove(entity);
            Logger.LogDebug("Postgres Deleted => Table[{Table}]: {Id}", _tableName, entity.Id);
        }
        else
        {
            entity.DeletedDateTime = DateTime.UtcNow;
            DbSet.Update(entity);
            Logger.LogDebug("Postgres SoftDeleted => Table[{Table}]: {Id}", _tableName, entity.Id);
        }

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<List<T>> DeleteManyAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        await EnsureTableAsync();

        var query = DbSet.Where(predicate);
        if (!hardDelete)
        {
            query = query.Where(x => x.DeletedDateTime == null);
        }

        var entities = await query.ToListAsync();
        if (entities.Count == 0)
        {
            return entities;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            if (hardDelete)
            {
                DbSet.RemoveRange(entities);
            }
            else
            {
                var utcNow = DateTime.UtcNow;
                foreach (var e in entities)
                {
                    e.DeletedDateTime = utcNow;
                }

                DbSet.UpdateRange(entities);
            }

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            foreach (var entity in entities)
            {
                Logger.LogDebug(
                    hardDelete
                        ? "Postgres Deleted => Table[{Table}]: {Id}"
                        : "Postgres SoftDeleted => Table[{Table}]: {Id}", _tableName, entity.Id);
            }

            return entities;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DeleteMany: {Message}", ex.Message);

            await transaction.RollbackAsync();

            return [];
        }
    }

    /// <inheritdoc/>
    public async Task DeleteManyAsync(ICollection<T> items, bool hardDelete = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ids = items.Select(x => x.Id).ToList();

        await DeleteManyAsync(x => ids.Contains(x.Id), hardDelete);
    }

    /// <inheritdoc/>
    public async Task TruncateTableAsync(bool restartIdentity = false, bool cascade = false)
    {
        await EnsureTableAsync();

        var suffix = (restartIdentity, cascade) switch
        {
            (true, true) => " RESTART IDENTITY CASCADE",
            (true, false) => " RESTART IDENTITY",
            (false, true) => " CASCADE",
            _ => "",
        };
        var sql = $"TRUNCATE TABLE {QuoteIdentifier(_tableName)}{suffix};";


        await _context.Database.ExecuteSqlRawAsync(sql);

        Logger.LogDebug("Truncated table {Table}", _tableName);
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(string id)
    {
        await EnsureTableAsync();

        var entity = await DbSet.FirstOrDefaultAsync(x => x.Id == id && x.DeletedDateTime != null);
        if (entity == null)
        {
            return;
        }

        entity.DeletedDateTime = null;
        DbSet.Update(entity);

        await _context.SaveChangesAsync();

        Logger.LogDebug("Postgres Restore => Table[{Table}]: {Id}", _tableName, entity.Id);
    }

    /// <inheritdoc/>
    public Task RestoreAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return RestoreAsync(entity.Id);
    }

    private static IQueryable<T> ApplySoftDelete(IQueryable<T> query, bool includeDeleted)
    {
        if (!includeDeleted)
        {
            query = query.Where(x => x.DeletedDateTime == null);
        }

        return query;
    }
}
