using BenchmarkDotNet.Running;
using Microsoft.Data.Sqlite;

namespace Sqlite.Benchmark;

public static class Program
{
    private const int RecordCount = 1_000;

    public static async Task Main(string[] args)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"sqlite-repository-benchmark-{Guid.NewGuid():N}.db");

        try
        {
            var connectionString = $"Data Source={databasePath}";
            var entities = BenchmarkEntity.CreateMany(RecordCount);

            await using (var repository = new SqliteRepository<BenchmarkEntity>(connectionString))
            {
                await repository.EnsureTableAsync();
                await repository.CreateIndexAsync(entity => entity.Category);
                await repository.InsertAsync(entities);
            }

            BenchmarkEnvironment.Configure(
                connectionString,
                entities[RecordCount / 2].Id);

            var summaries = BenchmarkSwitcher
                .FromAssembly(typeof(Program).Assembly)
                .Run(args);

            GC.KeepAlive(summaries);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
