using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flowamaz.Infrastructure.Persistence;

/// <summary>
/// Builds a value converter + comparer pair that maps a POCO to a jsonb column via
/// System.Text.Json. Used for plan limits/features so HasData seeding emits a JSON literal
/// into the migration (Npgsql owned-entity ToJson does not support HasData).
/// </summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<T, string> Converter<T>() => new(
        value => JsonSerializer.Serialize(value, Options),
        json => JsonSerializer.Deserialize<T>(json, Options)!);

    public static ValueComparer<T> Comparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => v == null ? 0 : JsonSerializer.Serialize(v, Options).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);
}
