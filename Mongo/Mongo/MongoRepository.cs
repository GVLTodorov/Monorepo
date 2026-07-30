using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using SortDirection = Mongo.Helpers.SortDirection;

namespace Mongo;

/// <summary>
/// Generic Repository for Mongo Collections
/// </summary>
/// <typeparam name="T"></typeparam>
public class MongoRepository<T> : IMongoRepository<T> where T : class, IMongoEntity
{
    private const string Data = "data";

    /// <summary>
    /// Collection
    /// </summary>
    protected IMongoCollection<T> Collection { get; set; }

    /// <summary>
    /// Database
    /// </summary>
    protected IMongoDatabase Database { get; set; }

    /// <summary>
    /// Client
    /// </summary>
    protected IMongoClient Client { get; set; }

    /// <summary>
    /// Logger
    /// </summary>
    public ILogger Logger { get; set; }

    /// <summary>
    /// Initialises a new instance of the <see cref="MongoRepository{T}"/> class
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="database"></param>
    public MongoRepository(string connectionString, string database)
    {
        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase($"{database.ToLower()}");
        Collection = Database.GetCollection<T>($"{MongoEntityExtensions.GetCollectionName<T>()}")!;
        Logger = new NullLoggerFactory().CreateLogger<MongoRepository<T>>();
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="MongoRepository{T}"/> class
    /// </summary>
    /// <param name="client"></param>
    /// <param name="database"></param>
    public MongoRepository(IMongoClient client, string database)
    {
        Client = client;
        Database = Client.GetDatabase($"{database.ToLower()}");
        Collection = Database.GetCollection<T>($"{MongoEntityExtensions.GetCollectionName<T>()}")!;
        Logger = new NullLoggerFactory().CreateLogger<MongoRepository<T>>();
    }

    #region Indexes

    /// <inheritdoc cref="CreateIndexAsync(Expression{Func{T, object}})" />
    public async Task CreateIndexAsync(Expression<Func<T, object>> field)
    {
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<T>(Builders<T>.IndexKeys.Ascending(field)!));
    }

    /// <inheritdoc cref="CreateIndexAsync(Expression{Func{T, object}}, TimeSpan)" />
    public async Task CreateIndexAsync(Expression<Func<T, object>> field, TimeSpan expiresAfter)
    {
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<T>(Builders<T>.IndexKeys.Ascending(field)!, new CreateIndexOptions()
        {
            ExpireAfter = expiresAfter
        }));
    }

    /// <inheritdoc cref="CreateIndexAsync(IEnumerable{Expression{Func{T, object}}}, Expression{Func{T, bool}}, bool)" />
    public async Task CreateIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false)
    {
        var fieldList = fields.ToList();
        if (!fieldList.Any())
        {
            throw new ArgumentException("Index must have at least one field", nameof(fields));
        }

        var keys = BuildIndexKeysDefinition(fields);

        var options = new CreateIndexOptions<T>()
        {
            Unique = unique
        };

        if (filter != null)
        {
            options.PartialFilterExpression = filter!;
        }

        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<T>(keys!, options));
    }

    /// <inheritdoc cref="CreateIndexAsync(IEnumerable{ValueTuple{Expression{Func{T, object}}, SortDirection}}, Expression{Func{T, bool}}, bool)" />
    public async Task CreateIndexAsync(
        IEnumerable<(Expression<Func<T, object>> PropertyExpression, SortDirection Direction)> fields,
        Expression<Func<T, bool>>? filter = null,
        bool unique = false)
    {
        var fieldList = fields.ToList();

        if (!fieldList.Any())
        {
            throw new ArgumentException("Index must have at least one field", nameof(fields));
        }

        var first = fieldList[0];
        var keys = first.Direction == SortDirection.Descending
            ? Builders<T>.IndexKeys.Descending(first.PropertyExpression)
            : Builders<T>.IndexKeys.Ascending(first.PropertyExpression);

        keys = fieldList.Skip(1)
            .Aggregate(keys, (current, field) => field.Direction == SortDirection.Descending
                ? current.Descending(field.PropertyExpression)
                : current.Ascending(field.PropertyExpression));

        var options = new CreateIndexOptions<T> { Unique = unique };

        if (filter is not null)
        {
            options.PartialFilterExpression = filter;
        }

        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<T>(keys, options));
    }

    /// <inheritdoc cref="RemoveIndexAsync(Expression{Func{T, object}})" />
    public async Task RemoveIndexAsync(Expression<Func<T, object>> field)
    {
        var memberName = ExpressionHelper.GetMemberName(field);
        if (string.IsNullOrEmpty(memberName))
        {
            return;
        }

        var indexCursor = await Collection.Indexes.ListAsync();
        var indexList = await indexCursor.ToListAsync();
        if (indexList.Count == 0)
        {
            return;
        }

        var index = indexList.Where(x => x["key"].ToString()!.ToLower().Contains(memberName.ToLower())).Select(x => x["name"]).FirstOrDefault();
        if (index == null)
        {
            return;
        }

        await Collection.Indexes.DropOneAsync(index.ToString());
    }

    /// <inheritdoc cref="RemoveIndexAsync(IEnumerable{Expression{Func{T, object}}}, Expression{Func{T, bool}}, bool)" />
    public async Task RemoveIndexAsync(IEnumerable<Expression<Func<T, object>>> fields, Expression<Func<T, bool>>? filter = null, bool unique = false)
    {
        var fieldList = fields.ToList();
        if (!fieldList.Any())
        {
            throw new ArgumentException("Index must have at least one field", nameof(fields));
        }

        // Build the index key definitions - we can't rely on property names as they don't always match (e.g. TenantId property uses _tid)
        var render = new RenderArgs<T>(Collection.DocumentSerializer, BsonSerializer.SerializerRegistry);
        var expectedKeys = 
            BuildIndexKeysDefinition(fieldList).
            Render(render);

        var expectedFilter = filter == null ?
        null :
            ((FilterDefinition<T>)filter).Render(render);

        var indexesBson = await (await Collection.Indexes.ListAsync()).ToListAsync();
        var indexes = indexesBson.Select(bson => BsonSerializer.Deserialize<IndexModel>(bson));

        var index = indexes.FirstOrDefault(index =>
            index.Key == expectedKeys
            && index.PartialFilterExpression == expectedFilter
            && index.Unique == unique);

        if (index == null)
        {
            return;
        }

        await Collection.Indexes.DropOneAsync(index.Name);
    }

    #endregion

    /// <inheritdoc cref="GetAllAsync{TProjection}(ProjectionDefinition{T, TProjection}, Expression{Func{T, bool}}, bool)" />
    public async Task<List<TProjection>> GetAllAsync<TProjection>(ProjectionDefinition<T, TProjection> projection,
        Expression<Func<T, bool>>? predicate = null, bool includeDeletes = false)
    {
        var predicateFilter = BuildPredicateOrDefault(predicate);
        predicateFilter = predicateFilter.AndAlso(BuildSoftDeleteExpression(includeDeletes));

        var findAsync = await Collection.FindAsync(predicateFilter, new FindOptions<T, TProjection>() { Projection = projection });
        var entities =
            await findAsync
                .ToListAsync();
        Logger.LogDebug("GetAll Count: {entitiesCount}", entities.Count);

        var entityList = entities.ToList();

        return entityList;
    }

    /// <inheritdoc cref="GetAllAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    public async Task<List<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null, Expression<Func<T, object>>? orderBy = null, SortDirection sortDirection = SortDirection.Ascending, bool includeDeletes = false)
    {
        var orderByFilter = BuildOrderByOrDefault(orderBy);
        var predicateFilter = BuildPredicateOrDefault(predicate);
        predicateFilter = predicateFilter.AndAlso(BuildSoftDeleteExpression(includeDeletes));

        var findAsync = await Collection.FindAsync(predicateFilter);
        var entities =
            await findAsync
                .ToListAsync();
        Logger.LogDebug("GetAll Count: {entitiesCount}", entities.Count);

        var results = sortDirection == SortDirection.Ascending
            ? entities.AsQueryable().OrderBy(orderByFilter)
            : entities.AsQueryable().OrderByDescending(orderByFilter);

        var resultList = results.ToList();

        return resultList;
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
        return GetPagedListAsync(
            pageIndex,
            pageSize,
            predicate,
            [new()
            {
                Expression = BuildOrderByOrDefault(orderBy), 
                SortDirection = sortDirection
            }],
            includeDeletes);
    }

    /// <inheritdoc cref="GetPagedListAsync(int, int, Expression{Func{T, bool}}, SortExpression{T}[], bool)" />
    public async Task<IPagedList<T>> GetPagedListAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? predicate,
        SortExpression<T>[] orderBy,
        bool includeDeletes = false)
    {
        // Switch to 0 based indexing for the query
        pageIndex -= 1;

        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        var predicateFilter = BuildPredicateAsFilterDefinition(predicate);
        var orderByFilter = BuildOrderByAsFilterDefinition(orderBy);
        var softDeleteFilter = BuildSoftDeleteFilterDefinition(includeDeletes);

        var filter = predicateFilter & softDeleteFilter;

        // Do not use an Aggregate for paged list.
        // Aggregates are bound by the maximum document size of 16MB. If we want a page of e.g. 50 documents we can easily hit this limit.

        var totalCount = await Collection.CountDocumentsAsync(filter);

        var result = await Collection
            .Find(filter)
            .Sort(orderByFilter)
            .Skip(pageIndex * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var totalPages = 0;
        if (totalCount > 0)
        {
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        }

        Logger.LogDebug($"GetPagedList Count: {totalCount}");

        var pagedList = new PagedList<T>(result, pageIndex + 1, pageSize, totalPages, totalCount);

        return pagedList;
    }

    /// <inheritdoc cref="GetFirstOrDefaultAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    public async Task<T?> GetFirstOrDefaultAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null, 
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeleted = false)
    {
        var predicateFilter = BuildPredicateAsFilterDefinition(predicate);
        var orderByFilter = BuildOrderByAsFilterDefinition(orderBy, sortDirection);
        var softDeleteFilter = BuildSoftDeleteFilterDefinition(includeDeleted);

        var dataFacetValue = new[]
        {
                PipelineStageDefinitionBuilder.Sort(orderByFilter),
                PipelineStageDefinitionBuilder.Match(softDeleteFilter),
                PipelineStageDefinitionBuilder.Match(predicateFilter)
            };

        var dataFacet = AggregateFacet.Create(Data, PipelineDefinition<T, T>.Create(dataFacetValue));
        var aggregation = await Collection.Aggregate()
            .Match(predicateFilter)!
            .Facet(dataFacet)
            .FirstOrDefaultAsync();

        var entity = aggregation.Facets.First(x => x.Name == Data).Output<T>().FirstOrDefault();

        return entity;
    }

    /// <inheritdoc cref="GetSingleOrDefaultAsync(Expression{Func{T, bool}})" />
    public async Task<T?> GetSingleOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        var cursor = await Collection.FindAsync(BuildPredicateAsFilterDefinition(predicate));
        var entity = await cursor.SingleOrDefaultAsync();

        return entity;
    }

    /// <inheritdoc cref="GetByIdAsync(string)" />
    public async Task<T?> GetByIdAsync(string id)
    {
        var entity = await GetFirstOrDefaultAsync(p => p.Id == id);

        return entity;
    }

    /// <inheritdoc cref="InsertAsync(T)" />
    public async Task InsertAsync(T entity)
    {

        entity.CreatedDateTime = DateTime.UtcNow;

        await Collection.InsertOneAsync(entity);

        Logger.LogDebug($"MongoDb Insert => Collection[{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");
    }

    /// <inheritdoc cref="InsertAsync(ICollection{T})" />
    public async Task InsertAsync(ICollection<T>? entities)
    {
        if (entities == null)
        {
            return;
        }

        foreach (var entity in entities)
        {
            entity.CreatedDateTime = DateTime.UtcNow;
        }

        await Collection.InsertManyAsync(entities);

        foreach (var entity in entities)
        {
            Logger.LogDebug($"MongoDb Insert => Collection [{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");
        }
    }

    /// <inheritdoc cref="UpdateAsync(T)" />
    public async Task UpdateAsync(T entity)
    {
        entity.UpdatedDateTime = DateTime.UtcNow;

        await Collection!.FindOneAndReplaceAsync(Builders<T>.Filter.Eq(p => p.Id, entity.Id), entity);

        Logger.LogDebug($"MongoDb Update => Collection [{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");
    }

    /// <inheritdoc cref="UpdateManyAsync(ICollection{T}, Dictionary{string, object})" />
    public async Task UpdateManyAsync(ICollection<T>? entities, Dictionary<string, object> updatedKeyValues)
    {
        if (entities?.Count == 0)
        {
            return;
        }

        using var session = await Client.StartSessionAsync(new ClientSessionOptions()
        {
            Snapshot = false,
        });
        session.StartTransaction();

        try
        {
            var utcNow = DateTime.UtcNow;
            var ids = entities?.Select(z => z.Id);

            var updateBuilderList = new List<UpdateDefinition<T>>
                {
                    Builders<T>.Update
                               .Set(entity => entity.UpdatedDateTime, utcNow)
                };

            foreach (var updatedKeyValue in updatedKeyValues)
            {
                updateBuilderList.Add(Builders<T>.Update.Set(updatedKeyValue.Key, updatedKeyValue.Value));
            }

            var updateBuilder = Builders<T>.Update.Combine(updateBuilderList);

            var updateResult = await Collection.UpdateManyAsync(session, Builders<T>.Filter.In(p => p.Id, ids), updateBuilder);

            if (updateResult.ModifiedCount == entities?.Count)
            {
                await session.CommitTransactionAsync();

                var type = typeof(T);
                var cachedProps = new Dictionary<string, PropertyInfo>();
                foreach (var entity in entities)
                {
                    //update the actual entity object passed through
                    entity.UpdatedDateTime = utcNow;

                    foreach (var updatedKeyValue in updatedKeyValues)
                    {
                        if (!cachedProps.TryGetValue(updatedKeyValue.Key, out var prop))
                        {
                            prop = type.GetProperty(updatedKeyValue.Key, BindingFlags.Public | BindingFlags.Instance);
                            cachedProps.Add(updatedKeyValue.Key, prop);
                        }

                        if (prop?.CanWrite == true)
                        {
                            prop.SetValue(entity, updatedKeyValue.Value);
                        }
                    }

                    Logger.LogDebug("MongoDb Update => Collection [{collectionName}]: {id}", MongoEntityExtensions.GetCollectionName<T>(), entity.Id);
                }
            }
            else
            {
                Logger.LogError("Error while executing UpdateMany. Updated count does not match entities count. Transaction aborted.");
                await session.AbortTransactionAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error writing to MongoDB: {exception}", ex.Message);
            await session.AbortTransactionAsync();
        }
    }

    /// <inheritdoc cref="PullAsync(FieldDefinition{T}, object, ICollection{T}, Expression{Func{T, object}})" />
    public async Task PullAsync(FieldDefinition<T> field,
                                object value,
                                ICollection<T>? entitiesToBeUpdated = null,
                                Expression<Func<T, object>>? fieldExistsFilter = null)
    {
        var utcNow = DateTime.UtcNow;
        var filter = Builders<T>.Filter.Empty;

        if (fieldExistsFilter != null)
        {
            filter &= Builders<T>.Filter.Exists(fieldExistsFilter, true);
        }

        if (entitiesToBeUpdated != null)
        {
            filter &= Builders<T>.Filter.In(p => p.Id, entitiesToBeUpdated.Select(z => z.Id));
        }

        var updateBuilderList = new List<UpdateDefinition<T>>
            {
                Builders<T>.Update
                            .Set(entity => entity.UpdatedDateTime, utcNow)
                            .Pull(field, value)
            };


        var updateBuilder = Builders<T>.Update.Combine(updateBuilderList);
        var updateResult = await Collection.UpdateManyAsync(filter, updateBuilder);

        Logger.LogInformation(
            "MongoDb Pull => Collection[{Collection}]: Modified {ModifiedCount} document(s)",
            MongoEntityExtensions.GetCollectionName<T>(),
            updateResult.ModifiedCount);
    }

    /// <inheritdoc cref="PullAsync{TItem}(Expression{Func{T, IEnumerable{TItem}}}, Expression{Func{TItem, bool}}, Expression{Func{T, bool}}, bool)" />
    public async Task PullAsync<TItem>(
        Expression<Func<T, IEnumerable<TItem>>> field,
        Expression<Func<TItem, bool>>? fieldFilter = null,
        Expression<Func<T, bool>>? documentPredicate = null,
        bool includeDeleted = false)
    {
        var utcNow = DateTime.UtcNow;
        fieldFilter ??= _ => true;

        var predicateFilter = BuildPredicateOrDefault(documentPredicate);
        predicateFilter = predicateFilter.AndAlso(BuildSoftDeleteExpression(includeDeleted));

        var update = Builders<T>.Update
            .Set(entity => entity.UpdatedDateTime, utcNow)
            .PullFilter(field, fieldFilter);

        await Collection.UpdateManyAsync(predicateFilter, update);
    }

    /// <inheritdoc cref="RestoreAsync(string)" />
    public async Task RestoreAsync(string id)
    {
        var entity = await Collection.FindOneAndUpdateAsync(
            x => x.Id == id && x.DeletedDateTime != null,
            new UpdateDefinitionBuilder<T>().Set(x => x.DeletedDateTime, null),
            new()
            {
                ReturnDocument = ReturnDocument.After
            });

        if (entity != null)
        {
            Logger.LogDebug($"MongoDb Restore => Collection[{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");
        }
    }

    /// <inheritdoc cref="RestoreAsync(T)" />
    public async Task RestoreAsync(T entity)
    {
        await RestoreAsync(entity.Id);
    }

    /// <inheritdoc cref="DeleteByIdAsync(string, bool)" />
    public async Task DeleteByIdAsync(string id, bool hardDelete = false)
    {
        await DeleteOneAsync(x => x.Id == id, hardDelete);
    }

    /// <inheritdoc cref="DeleteAsync(T, bool)" />
    public async Task DeleteAsync(T entity, bool hardDelete = false)
    {
        await DeleteOneAsync(x => x.Id == entity.Id, hardDelete);
    }

    /// <inheritdoc cref="DeleteOneAsync(Expression{Func{T, bool}}, bool)" />
    public async Task DeleteOneAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false)
    {
        if (!hardDelete)
        {
            var entity = await Collection.FindOneAndUpdateAsync(
                predicate.AndAlso(x => x.DeletedDateTime == null),
                new UpdateDefinitionBuilder<T>().Set(x => x.DeletedDateTime, DateTime.UtcNow),
                new()
                {
                    ReturnDocument = ReturnDocument.After
                });

            if (entity != null)
            {
                Logger.LogDebug($"MongoDb SoftDeleted => Collection[{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");
            }
        }
        else
        {
            var entity = await Collection.FindOneAndDeleteAsync(predicate);

            if (entity != null)
            {
                Logger.LogDebug($"MongoDb Deleted => Collection[{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");
            }
        }
    }

    /// <inheritdoc cref="DeleteManyAsync(Expression{Func{T, bool}}, bool)" />
    public async Task<List<T>> DeleteManyAsync(Expression<Func<T, bool>> predicate, bool hardDelete = false)
    {
        if (hardDelete == false)
        {
            using var session = await Client.StartSessionAsync(new ClientSessionOptions()
            {
                Snapshot = false,
            });
            session.StartTransaction();

            try
            {
                var entities = await GetAllAsync(predicate, includeDeletes: false);
                entities.ForEach(x => x.DeletedDateTime = DateTime.UtcNow);

                var updateResult = await Collection.UpdateManyAsync(
                    session,
                    predicate.AndAlso(x => x.DeletedDateTime == null),
                    new UpdateDefinitionBuilder<T>().Set(x => x.DeletedDateTime, DateTime.UtcNow));

                if (updateResult.ModifiedCount == entities.Count)
                {
                    await session.CommitTransactionAsync();

                    foreach (var entity in entities)
                        Logger.LogDebug($"MongoDb SoftDeleted => Collection [{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");

                    return entities;
                }

                Logger.LogError("Error while  soft DeleteMany. Deleted count does not match entities count. Transaction aborted.");
                await session.AbortTransactionAsync();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error writing to MongoDB: " + e.Message);
                await session.AbortTransactionAsync();
            }
        }
        else
        {
            using var session = await Client.StartSessionAsync();
            session.StartTransaction();

            try
            {
                var entities = await GetAllAsync(predicate, includeDeletes: true);

                var deleteResult = await Collection.DeleteManyAsync(session, predicate);

                if (deleteResult.DeletedCount == entities.Count)
                {
                    await session.CommitTransactionAsync();

                    foreach (var entity in entities)
                        Logger.LogDebug($"MongoDb Deleted => Collection [{MongoEntityExtensions.GetCollectionName<T>()}]: {entity.Id}");

                    return entities;
                }

                Logger.LogError("Error while  hard DeleteMany. Deleted count does not match entities count. Transaction aborted.");
                await session.AbortTransactionAsync();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error writing to MongoDB: " + e.Message);
                await session.AbortTransactionAsync();
            }
        }

        return [];
    }

    /// <inheritdoc cref="DeleteManyAsync(ICollection{T}, bool)" />
    public async Task DeleteManyAsync(ICollection<T> items, bool hardDelete = false)
    {
        var identifiers = items.Select(x => x.Id);

        await DeleteManyAsync(x => identifiers.Contains(x.Id), hardDelete);
    }

    /// <inheritdoc cref="CountAsync(Expression{Func{T, bool}})" />
    public async Task<long> CountAsync(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var count = await Collection.CountDocumentsAsync(BuildPredicateAsFilterDefinition(predicate));

        return count;
    }

    /// <inheritdoc cref="ExistsAsync(Expression{Func{T, bool}})" />
    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var entity = await GetFirstOrDefaultAsync(predicate);

        return entity != null;
    }

    #region Predicate/FilterDefinition Builders

    /// <summary>
    /// Converts a Predicate Expression into a <see cref="FilterDefinition{TDocument}"/>
    /// </summary>
    /// <param name="predicate">Predicate Expression</param>
    /// <returns><see cref="FilterDefinition{TDocument}"/></returns>
    private FilterDefinition<T> BuildPredicateAsFilterDefinition(Expression<Func<T, bool>>? predicate)
    {
        var filter = Builders<T>.Filter.Empty;

        if (predicate != null)
        {
            filter &= Builders<T>.Filter.Where(predicate);
        }
        var render = new RenderArgs<T>(Collection.DocumentSerializer, BsonSerializer.SerializerRegistry);
        Logger.LogDebug($"MongoDb Query => Collection [" +
                        $"{MongoEntityExtensions.GetCollectionName<T>()}]: {
                            filter.Render(render).ToJson()}");

        return filter;
    }

    /// <summary>
    /// Converts a Predicate Expression into a <see cref="FilterDefinition{TDocument}"/>
    /// </summary>
    /// <param name="predicate">Predicate Expression</param>
    /// <returns><see cref="FilterDefinition{TDocument}"/></returns>
    private Expression<Func<T, bool>> BuildPredicateOrDefault(Expression<Func<T, bool>>? predicate)
    {
        var filter = predicate ?? (entity => true);

        Logger.LogDebug(
            "MongoDb Query => Collection [{collectionName}]: {filterBody}",
            MongoEntityExtensions.GetCollectionName<T>(), filter.Body);

        return filter;
    }

    /// <summary>
    /// Converts a single Order By Expression into a <see cref="SortDefinition{TDocument}"/>
    /// </summary>
    /// <param name="orderBy">Order By Expression</param>
    /// <param name="direction">Sort Direction</param>
    /// <returns><see cref="SortDefinition{TDocument}"/></returns>
    private SortDefinition<T> BuildOrderByAsFilterDefinition(Expression<Func<T, object>>? orderBy, SortDirection direction)
    {
        return BuildOrderByAsFilterDefinition(
            [new()
            {
                Expression = BuildOrderByOrDefault(orderBy), 
                SortDirection = direction
            }]);
    }

    /// <summary>
    /// Converts a set of Order By Expressions into a <see cref="SortDefinition{TDocument}"/>
    /// </summary>
    /// <param name="orderBy">Order By Expressions</param>
    /// <returns><see cref="SortDefinition{TDocument}"/></returns>
    private SortDefinition<T> BuildOrderByAsFilterDefinition(SortExpression<T>[] orderBy)
    {
        var builder = Builders<T>.Sort;

        SortDefinition<T> sort;
        if (orderBy.Length == 0)
        {
            sort = builder.Ascending(BuildOrderByOrDefault(null));
        }
        else
        {
            var sortDefinitions = 
                orderBy.Select(sortExpression => sortExpression.SortDirection == SortDirection.Ascending ?
                builder.Ascending(sortExpression.Expression) :
                builder.Descending(sortExpression.Expression));

            sort = builder.Combine(sortDefinitions);
        }

        var render = new RenderArgs<T>(Collection.DocumentSerializer, BsonSerializer.SerializerRegistry);
        Logger.LogDebug("OrderBy => Collection [{collectionName}]: {orderBody}",
            MongoEntityExtensions.GetCollectionName<T>(),
            sort.Render(render).ToJson());

        return sort;
    }

    /// <summary>
    /// Converts an Order By Expression into a <see cref="SortDefinition{TDocument}"/>
    /// </summary>
    /// <param name="orderBy">Order By Expression</param>
    /// <returns><see cref="SortDefinition{TDocument}"/></returns>
    private Expression<Func<T, object>> BuildOrderByOrDefault(Expression<Func<T, object>>? orderBy)
    {
        if (orderBy != null)
        {
            return orderBy;
        }

        Expression<Func<T, object>> order = r => r.CreatedDateTime;
        Logger.LogDebug(
            "MongoDb OrderBy => Collection [{collectionName}]: {orderBody}",
            MongoEntityExtensions.GetCollectionName<T>(), order.Body);

        return order;
    }

    /// <summary>
    /// Builds a <see cref="FilterDefinition{TDocument}"/> enabling/disabling the Soft Delete Filter
    /// </summary>
    /// <param name="includeDeleted">Include Deleted Documents</param>
    /// <returns><see cref="FilterDefinition{TDocument}"/></returns>
    private FilterDefinition<T> BuildSoftDeleteFilterDefinition(bool includeDeleted)
    {
        var filter = Builders<T>.Filter.Empty;

        if (includeDeleted == false)
        {
            filter = Builders<T>.Filter.Where(p => p.DeletedDateTime == null);
        }

        return filter;
    }

    /// <summary>
    /// Builds a <see cref="FilterDefinition{TDocument}"/> enabling/disabling the Soft Delete Filter
    /// </summary>
    /// <param name="includeDeleted"></param>
    /// <returns></returns>
    private static Expression<Func<T, bool>> BuildSoftDeleteExpression(bool includeDeleted)
    {
        Expression<Func<T, bool>> filter = entity => true;

        if (!includeDeleted)
        {
            filter = filter.AndAlso(p => p.DeletedDateTime == null);
        }

        return filter;
    }

    #endregion

    private IndexKeysDefinition<T> BuildIndexKeysDefinition(IEnumerable<Expression<Func<T, object>>> fields)
    {
        var list = fields.ToList();
        var keys = Builders<T>.IndexKeys.Ascending(list.First());
        foreach (var field in list.Skip(1))
        {
            keys = keys.Ascending(field);
        }

        return keys;
    }

    [BsonIgnoreExtraElements]
    private class IndexModel
    {
        [BsonElement("v")]
        public int V { get; set; }

        [BsonElement("name")]
        public string? Name { get; set; }

        [BsonElement("key")]
        public BsonDocument? Key { get; set; }

        [BsonElement("unique")]
        public bool Unique { get; set; }

        [BsonElement("partialFilterExpression")]
        public BsonDocument? PartialFilterExpression { get; set; }
    }
}

/// <summary>Marker interface for MongoDB repositories.</summary>
public interface IMongoRepository
{
}

/// <summary>Defines the MongoDB repository contract for an entity type.</summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IMongoRepository<T> : IMongoRepository where T : class, IMongoEntity
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

    /// <inheritdoc cref="PullAsync(FieldDefinition{T}, object, ICollection{T}, Expression{Func{T, object}})" />
    Task PullAsync(
        FieldDefinition<T> field,
        object value,
        ICollection<T>? entitiesToBeUpdated = null,
        Expression<Func<T, object>>? fieldExistsFilter = null);

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

    /// <inheritdoc cref="RestoreAsync(T)" />
    Task RestoreAsync(T entity);

    /// <inheritdoc cref="RestoreAsync(string)" />
    Task RestoreAsync(string id);

    /// <inheritdoc cref="CountAsync(Expression{Func{T, bool}})" />
    Task<long> CountAsync(Expression<Func<T, bool>> predicate);

    /// <inheritdoc cref="ExistsAsync(Expression{Func{T, bool}})" />
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);

    /// <inheritdoc cref="GetByIdAsync(string)" />
    Task<T?> GetByIdAsync(string id);

    /// <inheritdoc cref="GetAllAsync(Expression{Func{T, bool}}, Expression{Func{T, object}}, SortDirection, bool)" />
    Task<List<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeDeletes = false);

    /// <inheritdoc cref="GetAllAsync{TProjection}(ProjectionDefinition{T, TProjection}, Expression{Func{T, bool}}, bool)" />
    Task<List<TProjection>> GetAllAsync<TProjection>(
        ProjectionDefinition<T, TProjection> projection,
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
