namespace Api.Infrastructure;

/// <summary>
/// Markers attached to outbound LLM requests so the resilience pipeline can tell
/// a blocking generation from a streaming one.
/// </summary>
public static class ChatRequestKinds
{
    /// <summary>
    /// Set to true on streaming requests. The retry strategy skips them: once a
    /// stream is opened, replaying the POST would run the generation a second
    /// time for no benefit, and the caller has already started reading tokens.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> IsStreaming = new("Api.Chat.IsStreaming");

    /// <summary>
    /// Reads the marker, defaulting to false for requests that never set it
    /// (for example the blocking path, which builds its request implicitly).
    /// </summary>
    public static bool IsStreamingRequest(HttpRequestMessage? request)
        => request is not null
           && request.Options.TryGetValue(IsStreaming, out var isStreaming)
           && isStreaming;
}
