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