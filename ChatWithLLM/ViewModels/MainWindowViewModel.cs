using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChatWithLLM.Models;
using ChatWithLLM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatWithLLM.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ChatSessionStore _sessionStore;
    private readonly AppSettingsStore _settingsStore;
    private GeminiClient? _gemini;
    private string? _geminiApiKey;
    private double _lastTemperature;
    private int _lastMaxTokens;
    private string? _lastModelName;
    private Window? _window;

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    // Mevcut modeller listesi
    public string[] AvailableModels { get; } =
    [
        "gemini-2.0-flash"
    ];

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _selectedModel = "gemini-2.0-flash";

    [ObservableProperty]
    private double _temperature = 0.7;

    [ObservableProperty]
    private int _maxOutputTokens = 2048;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _attachedFileName = string.Empty;

    [ObservableProperty]
    private string _attachedFileContent = string.Empty;

    public bool HasAttachment => !string.IsNullOrEmpty(AttachedFileName);

    partial void OnAttachedFileNameChanged(string value) => OnPropertyChanged(nameof(HasAttachment));

    partial void OnUserInputChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _typingText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public MainWindowViewModel()
    {
        _sessionStore = new ChatSessionStore();
        _settingsStore = new AppSettingsStore();

        var settings = _settingsStore.Load();
        ApiKey = settings.GeminiApiKey ?? string.Empty;
        Temperature = settings.Temperature;
        MaxOutputTokens = settings.MaxOutputTokens;
        SelectedModel = settings.ModelName;

        foreach (var message in _sessionStore.Load())
            Messages.Add(ToItem(message));

        if (string.IsNullOrWhiteSpace(ApiKey) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
            ErrorText = "Gemini API Key girin (üst bardan) veya GEMINI_API_KEY ortam değişkenini ayarlayın.";
    }

    private readonly DispatcherTimer _typingTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _typingDotCount;

    private GeminiClient EnsureGeminiClient()
    {
        var apiKey = !string.IsNullOrWhiteSpace(ApiKey)
            ? ApiKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API Key ayarlı değil.");

        var needsRebuild = _gemini is null
            || !string.Equals(_geminiApiKey, apiKey, StringComparison.Ordinal)
            || Math.Abs(_lastTemperature - Temperature) > 0.01
            || _lastMaxTokens != MaxOutputTokens
            || !string.Equals(_lastModelName, SelectedModel, StringComparison.Ordinal);

        if (needsRebuild)
        {
            _geminiApiKey = apiKey;
            _lastTemperature = Temperature;
            _lastMaxTokens = MaxOutputTokens;
            _lastModelName = SelectedModel;
            _gemini = new GeminiClient(new System.Net.Http.HttpClient(), apiKey, SelectedModel, Temperature, MaxOutputTokens);
        }

        return _gemini;
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        ErrorText = string.Empty;

        var trimmedKey = (ApiKey ?? string.Empty).Trim();
        ApiKey = trimmedKey;

        _settingsStore.Save(new AppSettings
        {
            GeminiApiKey = string.IsNullOrWhiteSpace(trimmedKey) ? null : trimmedKey,
            Temperature = Temperature,
            MaxOutputTokens = MaxOutputTokens,
            ModelName = SelectedModel
        });

        // Client'ı yeniden oluşturmaya zorla
        _gemini = null;

        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void SaveKey()
    {
        ErrorText = string.Empty;

        var trimmed = (ApiKey ?? string.Empty).Trim();
        ApiKey = trimmed;

        _settingsStore.Save(new AppSettings
        {
            GeminiApiKey = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed,
            Temperature = Temperature,
            MaxOutputTokens = MaxOutputTokens,
            ModelName = SelectedModel
        });
    }

    public void SetWindow(Window window)
    {
        _window = window;
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        if (_window is null)
            return;

        var storageProvider = _window.StorageProvider;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Dosya Seç",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Metin Dosyaları") { Patterns = new[] { "*.txt", "*.md", "*.json", "*.xml", "*.csv", "*.log", "*.cs", "*.py", "*.js", "*.ts", "*.html", "*.css" } },
                new FilePickerFileType("Tüm Dosyalar") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0)
            return;

        var file = files[0];
        AttachedFileName = file.Name;

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            AttachedFileContent = await reader.ReadToEndAsync();

            if (AttachedFileContent.Length > 50000)
            {
                AttachedFileContent = AttachedFileContent[..50000] + "\n\n[... dosya çok uzun, kısaltıldı ...]";
            }
        }
        catch (Exception ex)
        {
            ErrorText = $"Dosya okunamadı: {ex.Message}";
            AttachedFileName = string.Empty;
            AttachedFileContent = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveAttachment()
    {
        AttachedFileName = string.Empty;
        AttachedFileContent = string.Empty;
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));

        if (value)
        {
            _typingDotCount = 0;
            TypingText = "Yazıyor";

            _typingTimer.Tick -= TypingTimerOnTick;
            _typingTimer.Tick += TypingTimerOnTick;
            _typingTimer.Start();
        }
        else
        {
            _typingTimer.Stop();
            _typingTimer.Tick -= TypingTimerOnTick;
            TypingText = string.Empty;
        }
    }

    private void TypingTimerOnTick(object? sender, EventArgs e)
    {
        _typingDotCount = (_typingDotCount + 1) % 4;
        TypingText = "Yazıyor" + new string('.', _typingDotCount);
    }

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    private bool CanSend() => !IsBusy && (!string.IsNullOrWhiteSpace(UserInput) || HasAttachment);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        ErrorText = string.Empty;

        var input = (UserInput ?? string.Empty).Trim();

        // Dosya ekliyse mesaja ekle
        var messageText = input;
        if (HasAttachment)
        {
            var fileInfo = $"\n\n📎 **Eklenen Dosya: {AttachedFileName}**\n```\n{AttachedFileContent}\n```";
            messageText = string.IsNullOrWhiteSpace(input)
                ? $"Bu dosyayı incele:{fileInfo}"
                : $"{input}{fileInfo}";

            // Attachment'ı temizle
            AttachedFileName = string.Empty;
            AttachedFileContent = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(messageText))
            return;

        Messages.Add(new UserChatMessageItem(DateTimeOffset.Now, messageText));
        UserInput = string.Empty;
        SaveSession();

        IsBusy = true;
        SendCommand.NotifyCanExecuteChanged();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var gemini = EnsureGeminiClient();
            var reply = await gemini.GenerateAsync(ToModels(Messages), cts.Token);

            var full = string.IsNullOrWhiteSpace(reply) ? "(boş yanıt)" : reply;
            var modelItem = new ModelChatMessageItem(DateTimeOffset.Now, string.Empty);
            Messages.Add(modelItem);

            await TypewriterFillAsync(modelItem, full, cts.Token);
            SaveSession();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    private void SaveSession()
    {
        _sessionStore.Save(ToModels(Messages));
    }

    private static IReadOnlyList<ChatMessage> ToModels(ObservableCollection<ChatMessageItem> items)
    {
        return items
            .Select(i => new ChatMessage(i.Role, i.Text, i.Timestamp))
            .ToList();
    }

    private static ChatMessageItem ToItem(ChatMessage message)
    {
        return message.IsUser
            ? new UserChatMessageItem(message.Timestamp, message.Text)
            : new ModelChatMessageItem(message.Timestamp, message.Text);
    }

    private static async Task TypewriterFillAsync(ModelChatMessageItem target, string fullText, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(fullText.Length);
        var chunkSize = fullText.Contains("```") ? 8 : 3;

        for (var i = 0; i < fullText.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.Append(fullText[i]);

            if (i % chunkSize == 0 || i == fullText.Length - 1)
            {
                var snapshot = builder.ToString();
                await Dispatcher.UIThread.InvokeAsync(() => target.Text = snapshot);
                await Task.Delay(15, cancellationToken);
            }
        }
    }
}
