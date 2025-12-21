using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CyanTools.Model
{


    public class ApiRequest
    {
        [JsonPropertyName("messages")]
        public List<Message> Messages { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 100;

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.2;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseFormat ResponseFormat { get; set; }
    }

    public class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("json_schema")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseJsonSchema JsonSchema { get; set; }
    }

    public class ResponseJsonSchema
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("schema")]
        public JsonSchemaDefinition Schema { get; set; }

        [JsonPropertyName("strict")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Strict { get; set; }
    }

    public class JsonSchemaDefinition
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, JsonSchemaProperty> Properties { get; set; }

        [JsonPropertyName("required")]
        public List<string> Required { get; set; }

        [JsonPropertyName("additionalProperties")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool AdditionalProperties { get; set; }
    }

    public class JsonSchemaProperty
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("minimum")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Minimum { get; set; }

        [JsonPropertyName("maximum")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Maximum { get; set; }

        [JsonPropertyName("maxLength")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxLength { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public List<ContentPart> Content { get; set; }
    }

    public class ContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }

        [JsonPropertyName("image_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ImageUrlData ImageUrl { get; set; }
    }

    public class ImageUrlData
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }



    public class ApiResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; }
    }

    public class Choice
    {
        [JsonPropertyName("message")]
        public ResponseMessage Message { get; set; }
    }

    public class ResponseMessage
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }
    }



    public class AnalysisResult
    {
        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }
}
