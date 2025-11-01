namespace CanIHazHouze.Web.Services;

public class BackgroundActivityService
{
    private int _activeRequests = 0;
    private readonly object _lock = new();

    public event Action? OnActivityChanged;

    public bool HasActivity => _activeRequests > 0;
    public int ActiveRequestCount => _activeRequests;

    public void StartActivity()
    {
        lock (_lock)
        {
            _activeRequests++;
        }
        OnActivityChanged?.Invoke();
    }

    public void EndActivity()
    {
        lock (_lock)
        {
            if (_activeRequests > 0)
            {
                _activeRequests--;
            }
        }
        OnActivityChanged?.Invoke();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _activeRequests = 0;
        }
        OnActivityChanged?.Invoke();
    }
}
