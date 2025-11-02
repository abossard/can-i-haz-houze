namespace CanIHazHouze.Web.Services;

public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}

public class ToastMessage
{
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; }
    public DateTime CreatedAt { get; set; }
    public int DurationSeconds { get; set; }
}

public class ToastService
{
    public event Action<ToastMessage>? OnShow;
    
    private readonly List<ToastMessage> _toasts = new();

    public IReadOnlyList<ToastMessage> Toasts => _toasts.AsReadOnly();

    public void ShowToast(string message, ToastType type = ToastType.Info, int durationSeconds = 5)
    {
        var toast = new ToastMessage
        {
            Message = message,
            Type = type,
            CreatedAt = DateTime.UtcNow,
            DurationSeconds = durationSeconds
        };
        
        _toasts.Add(toast);
        OnShow?.Invoke(toast);
    }

    public void ShowSuccess(string message, int durationSeconds = 5)
        => ShowToast(message, ToastType.Success, durationSeconds);

    public void ShowError(string message, int durationSeconds = 7)
        => ShowToast(message, ToastType.Error, durationSeconds);

    public void ShowWarning(string message, int durationSeconds = 6)
        => ShowToast(message, ToastType.Warning, durationSeconds);

    public void ShowInfo(string message, int durationSeconds = 5)
        => ShowToast(message, ToastType.Info, durationSeconds);

    public void Clear()
    {
        _toasts.Clear();
    }

    public void Remove(ToastMessage toast)
    {
        _toasts.Remove(toast);
    }
}
