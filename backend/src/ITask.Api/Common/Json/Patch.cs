using System.Text.Json;
using System.Text.Json.Serialization;

namespace ITask.Api.Common.Json;

/// <summary>
/// A JSON property that may be absent. Absent leaves <see cref="HasValue"/> false; present
/// (including <c>null</c>) sets it, so PATCH bodies can tell "leave as is" from "clear".
/// </summary>
[JsonConverter(typeof(PatchConverterFactory))]
public readonly record struct Patch<T>(T Value)
{
    public bool HasValue { get; } = true;

    public static implicit operator Patch<T>(T value) => new(value);

    /// <summary>The value when present, otherwise <paramref name="current"/>.</summary>
    public T Or(T current) => HasValue ? Value : current;
}

public sealed class PatchConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(PatchConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class PatchConverter<T> : JsonConverter<Patch<T>>
    {
        public override bool HandleNull => true;

        public override Patch<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value.HasValue)
            {
                JsonSerializer.Serialize(writer, value.Value, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
