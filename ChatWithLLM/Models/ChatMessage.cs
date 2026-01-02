using System;
using System.Text.Json.Serialization;
using Avalonia.Layout;

namespace ChatWithLLM.Models;

public sealed class ChatMessage
{
    public ChatMessage(string role, string text, DateTimeOffset? timestamp = null)
    {
        Role = role;
        Text = text;
        Timestamp = timestamp ?? DateTimeOffset.Now;
    }

    [JsonPropertyName("role")]
    public string Role { get; }

    [JsonPropertyName("text")]
    public string Text { get; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; }

    [JsonIgnore]
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    [JsonIgnore]
    public string ClassNames => IsUser ? "chat-bubble user" : "chat-bubble model";
}
