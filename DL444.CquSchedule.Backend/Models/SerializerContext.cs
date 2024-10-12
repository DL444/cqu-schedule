using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Models;

namespace DL444.CquSchedule.Backend.Models
{
    [JsonSerializable(typeof(Credential))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class CredentialSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<Credential>
    {
        public JsonTypeInfo<Credential> TypeInfo => this.Credential;
    }

    [JsonSerializable(typeof(UpstreamScheduleResponseModel))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class UpstreamScheduleResponseModelSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<UpstreamScheduleResponseModel>
    {
        public JsonTypeInfo<UpstreamScheduleResponseModel> TypeInfo => this.UpstreamScheduleResponseModel;
    }

    [JsonSerializable(typeof(UpstreamExamResponseModel))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class UpstreamExamResponseModelSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<UpstreamExamResponseModel>
    {
        public JsonTypeInfo<UpstreamExamResponseModel> TypeInfo => this.UpstreamExamResponseModel;
    }

    [JsonSerializable(typeof(UpstreamTermListResponseModel))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class UpstreamTermListResponseModelSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<UpstreamTermListResponseModel>
    {
        public JsonTypeInfo<UpstreamTermListResponseModel> TypeInfo => this.UpstreamTermListResponseModel;
    }

    [JsonSerializable(typeof(UpstreamTermResponseModel))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal sealed partial class UpstreamTermResponseModelSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<UpstreamTermResponseModel>
    {
        public JsonTypeInfo<UpstreamTermResponseModel> TypeInfo => this.UpstreamTermResponseModel;
    }

    [JsonSerializable(typeof(Response<IcsSubscription>))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
    internal sealed partial class IcsSubscriptionResponseSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<Response<IcsSubscription>>
    {
        public JsonTypeInfo<Response<IcsSubscription>> TypeInfo => this.ResponseIcsSubscription;
    }

    [JsonSerializable(typeof(Response<int>))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
    internal sealed partial class StatusOnlyResponseSerializerContext : JsonSerializerContext, IJsonSerializerContextTypeInfoSource<Response<int>>
    {
        public JsonTypeInfo<Response<int>> TypeInfo => this.ResponseInt32;
    }
}
