using System.Text.Json;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Persistence;

internal static class IntegrationEventSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>"Namespace.Type, Assembly" — stable across versions, resolvable with <see cref="Type.GetType(string)"/>.</summary>
    public static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
}
