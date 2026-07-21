using Microsoft.EntityFrameworkCore;
using Postgres.Helpers;

namespace Postgres;

/// <summary>
/// Entity Framework Core DbContext for a single entity type (used by PostgresRepository).
/// </summary>
public class PostgresDbContext<T> : DbContext where T : class, IPostgresEntity
{
    private readonly string? _connectionString;

    /// <summary>Creates a context that uses the supplied PostgreSQL connection string.</summary>
    public PostgresDbContext(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates a context with externally configured options. This is useful for
    /// dependency injection and for tests that use another EF Core provider.
    /// </summary>
    public PostgresDbContext(DbContextOptions<PostgresDbContext<T>> options)
        : base(options)
    {
    }

    /// <summary>Gets the entity set managed by this context.</summary>
    public DbSet<T> Entities => Set<T>();

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseNpgsql(_connectionString
                ?? throw new InvalidOperationException("A PostgreSQL connection string or configured DbContextOptions are required."));
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tableName = PostgresEntityExtensions.GetTableName<T>();
        modelBuilder.Entity<T>(e =>
        {
            e.ToTable(tableName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.CreatedDateTime).IsRequired();
            e.Property(x => x.UpdatedDateTime);
            e.Property(x => x.DeletedDateTime);
            e.Ignore(x => x.IsDeleted);
        });
    }
}
