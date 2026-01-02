using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatWithLLM.Models;

namespace ChatWithLLM.Services;

public sealed class ChatSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public ChatSessionStore(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultFilePath();
    }

    public IReadOnlyList<ChatMessage> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<ChatMessage>();

            var json = File.ReadAllText(_filePath);
            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(json, JsonOptions);
            return messages ?? (IReadOnlyList<ChatMessage>)Array.Empty<ChatMessage>();
        }
        catch
        {
            return Array.Empty<ChatMessage>();
        }
    }

    public void Save(IReadOnlyList<ChatMessage> messages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
        var json = JsonSerializer.Serialize(messages, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static string GetDefaultFilePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = AppContext.BaseDirectory;

        return Path.Combine(baseDir, "ChatWithLLM", "chat_session.json");
    }
}
