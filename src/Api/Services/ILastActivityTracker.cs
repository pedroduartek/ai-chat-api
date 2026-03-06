using System;

namespace Api.Services;

public interface ILastActivityTracker
{
    void Touch();
    DateTime GetLastActivityUtc();
}
