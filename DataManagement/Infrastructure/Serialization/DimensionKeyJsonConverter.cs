using System.Text.Json;
using System.Text.Json.Serialization;
using WAD.Runner.DataManagement.Domain.Dimensions;

namespace WAD.Runner.DataManagement.Infrastructure.Serialization;

/// <summary>
/// Allows System.Text.Json to use DimensionKey as a dictionary key
/// by emitting its .Value (string).
/// Deserialization is intentionally not supported here.
/// </summary>
public sealed class DimensionKeyJsonConverter : JsonConverter<DimensionKey>
{
    public override DimensionKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Deserialization of DimensionKey is not supported by this converter.");

    public override void Write(Utf8JsonWriter writer, DimensionKey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, DimensionKey value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);

    public override DimensionKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Deserialization of DimensionKey is not supported by this converter.");
}
