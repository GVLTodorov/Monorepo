using BenchmarkDotNet.Running;
using Testcontainers.MongoDb;

namespace Mongo.Benchmark;

public static class Program
{
    private const int RecordCount = 1_000;
    private const string DatabaseName = "repository_benchmark";

    public static async Task Main(string[] args)
    {
        await using var mongo = new MongoDbBuilder("mongo:6.0")
            .WithReplicaSet("rs0")
            .Build();

        await mongo.StartAsync();

        var connectionString = mongo.GetConnectionString();
        var repository = new MongoRepository<BenchmarkEntity>(connectionString, DatabaseName);
        var entities = BenchmarkEntity.CreateMany(RecordCount);

        await repository.CreateIndexAsync(entity => entity.Category);
        await repository.InsertAsync(entities);

        BenchmarkEnvironment.Configure(
            connectionString,
            DatabaseName,
            entities[RecordCount / 2].Id);

        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);

        GC.KeepAlive(summaries);
    }
}
