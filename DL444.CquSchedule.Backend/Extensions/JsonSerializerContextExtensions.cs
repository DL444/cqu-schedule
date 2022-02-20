using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace DL444.CquSchedule.Backend.Extensions
{
    internal interface IJsonSerializerContextTypeInfoSource<T>
    {
        JsonTypeInfo<T> TypeInfo { get; }
    }

    internal static class JsonSerializerContextExtension
    {
        public static string SerializeToString<T>(this IJsonSerializerContextTypeInfoSource<T> jsonSerializerContext, T obj)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            using Utf8JsonWriter writer = new Utf8JsonWriter(bufferWriter);
            jsonSerializerContext.TypeInfo.SerializeHandler(writer, obj);
            writer.Flush();
            return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
        }

        public static T DeserializeFromString<T>(this IJsonSerializerContextTypeInfoSource<T> jsonSerializerContext, string json)
            => JsonSerializer.Deserialize(json, jsonSerializerContext.TypeInfo);

        public static ValueTask<T> DeserializeFromStringAsync<T>(this IJsonSerializerContextTypeInfoSource<T> jsonSerializerContext, Stream jsonStream)
            => JsonSerializer.DeserializeAsync<T>(jsonStream, jsonSerializerContext.TypeInfo);

        public static IActionResult GetSerializedResponse<T>(this IJsonSerializerContextTypeInfoSource<T> jsonSerializerContext, T obj, int statusCode = 200)
        {
            return new ContentResult()
            {
                Content = jsonSerializerContext.SerializeToString(obj),
                ContentType = "application/json",
                StatusCode = statusCode
            };
        }
    }
}
