using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatWithLLM.ViewModels;

public abstract partial class ChatMessageItem : ObservableObject
{
    protected ChatMessageItem(DateTimeOffset timestamp, string initialText)
    {
        Timestamp = timestamp;
        _text = initialText;
    }

    public DateTimeOffset Timestamp { get; }

    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm");

    public abstract string Role { get; }

    public abstract string DisplayName { get; }

    public abstract string AvatarText { get; }

    [ObservableProperty]
    private string _text;
}

public sealed class UserChatMessageItem : ChatMessageItem
{
    public UserChatMessageItem(DateTimeOffset timestamp, string initialText)
        : base(timestamp, initialText)
    {
    }

    public override string Role => "user";

    public override string DisplayName => "You";

    public override string AvatarText => "Y";
}

public sealed class ModelChatMessageItem : ChatMessageItem
{
    public ModelChatMessageItem(DateTimeOffset timestamp, string initialText)
        : base(timestamp, initialText)
    {
    }

    public override string Role => "model";

    public override string DisplayName => "Gemini";

    public override string AvatarText => "G";
}
