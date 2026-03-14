using System;

namespace Api.Services.Warmup;

public class LastActivityTracker : ILastActivityTracker
{
    private readonly object _lock = new();
    private DateTime _lastActivityUtc;

    public LastActivityTracker()
    {
        // Initialize to MinValue so the keep-warm service will perform
        // an initial warmup after the configured interval even if no
        // user requests have been received yet.
        _lastActivityUtc = DateTime.MinValue;
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
