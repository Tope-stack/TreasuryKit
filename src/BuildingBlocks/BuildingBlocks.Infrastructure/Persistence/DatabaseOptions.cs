namespace TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string ConnectionStringName = "TreasuryKit";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Apply pending migrations at Api startup. Always on in Development.</summary>
    public bool MigrateOnStartup { get; set; }
}

/// <summary>Marks a module DbContext type so hosts can enumerate them (migrations, outbox).</summary>
public sealed record ModuleDbContextRegistration(Type ContextType, string Schema);
