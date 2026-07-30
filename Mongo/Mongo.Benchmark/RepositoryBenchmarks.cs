using BenchmarkDotNet.Attributes;
using Mongo.Helpers;

namespace Mongo.Benchmark;

[MemoryDiagnoser(displayGenColumns: false)]
[RankColumn]
public class RepositoryBenchmarks
{
    private MongoRepository<BenchmarkEntity> _repository = null!;
    private string _targetId = string.Empty;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _repository = new MongoRepository<BenchmarkEntity>(
            BenchmarkEnvironment.GetConnectionString(),
            BenchmarkEnvironment.GetDatabaseName());
        _targetId = BenchmarkEnvironment.GetTargetId();

        var recordCount = await _repository.CountAsync(_ => true);
        if (recordCount != 1_000)
        {
            throw new InvalidOperationException($"Expected 1,000 benchmark records, but found {recordCount}.");
        }
    }

    [Benchmark]
    public async Task<BenchmarkEntity?> GetByIdAsync()
    {
        var entity = await _repository.GetByIdAsync(_targetId);

        return entity;
    }

    [Benchmark]
    public async Task<List<BenchmarkEntity>> GetFilteredAsync()
    {
        var entities = await _repository.GetAllAsync(
            predicate: entity => entity.Category == "Even",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);

        return entities;
    }

    [Benchmark]
    public async Task<IPagedList<BenchmarkEntity>> GetPageAsync()
    {
        var page = await _repository.GetPagedListAsync(
            pageIndex: 10,
            pageSize: 25,
            predicate: entity => entity.Category == "Even",
            orderBy: entity => entity.Score,
            sortDirection: SortDirection.Descending);

        return page;
    }

    [Benchmark]
    public async Task<long> CountFilteredAsync()
    {
        var count = await _repository.CountAsync(entity => entity.Category == "Even");

        return count;
    }
}
