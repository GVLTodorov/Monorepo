using BenchmarkDotNet.Running;
using Testcontainers.PostgreSql;

namespace Postgres.Benchmark;

public static class Program
{
    private const int RecordCount = 1_000;

    public static async Task Main(string[] args)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15.1").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        await using var repository = new PostgresRepository<BenchmarkEntity>(connectionString);
        var entities = BenchmarkEntity.CreateMany(RecordCount);

        await repository.EnsureTableAsync();
        await repository.CreateIndexAsync(entity => entity.Category);
        await repository.InsertAsync(entities);

        BenchmarkEnvironment.Configure(
            connectionString,
            entities[RecordCount / 2].Id);

        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);

        GC.KeepAlive(summaries);
    }
}
