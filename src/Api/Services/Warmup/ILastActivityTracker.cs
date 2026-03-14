using System;

namespace Api.Services.Warmup;

public interface ILastActivityTracker
{
    void Touch();
    DateTime GetLastActivityUtc();
}
