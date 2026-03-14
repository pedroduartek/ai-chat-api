namespace Api.Application;

public sealed record ChatCompletionRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    bool Stream,
    ChatGenerationOptions Options);

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatGenerationOptions(
    double Temperature,
    double TopP,
    int TopK,
    double RepeatPenalty,
    int NumPredict,
    int NumCtx);
