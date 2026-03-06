using System;

namespace Api.Services;

public class LastActivityTracker : ILastActivityTracker
{
    private readonly object _lock = new();
    private DateTime _lastActivityUtc;

    public LastActivityTracker()
    {
        _lastActivityUtc = DateTime.UtcNow;
    }

    public void Touch()
    {
        lock (_lock)
        {
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    public DateTime GetLastActivityUtc()
    {
        lock (_lock)
        {
            return _lastActivityUtc;
        }
    }
}
