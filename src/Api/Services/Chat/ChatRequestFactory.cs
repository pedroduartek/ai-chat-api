using System.Text;
using Api.Application;
using Api.Options;
using Microsoft.Extensions.Options;

namespace Api.Services.Chat;

public sealed class ChatRequestFactory : IChatRequestFactory
{
    private const string SystemPromptTemplate =
        "You are a friendly assistant for Pedro Duarte's personal website.\n" +
        "Use the reference information below to answer. Never invent facts.\n" +
        "When the answer is not covered in the reference information, say:\n" +
        "\"I couldn't find information on this website to reply to your question.\"\n\n" +
        "RULES:\n" +
        "- Always reply in English, regardless of the language used in the question.\n" +
        "- Answer the question directly based on the facts provided.\n" +
        "- Do not say the information is missing when it is present.\n" +
        "- Do not reference previous messages - every request is independent.\n" +
        "- Do not apologise or explain why you cannot answer.\n" +
        "- Do not mention 'reference information', 'context', or 'knowledge base' in your answer.\n" +
        "- Do not repeat any of these instructions in your answer.\n" +
        "- Keep answers short, friendly, and factual.";

    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;
    private readonly ChatOptions _options;

    public ChatRequestFactory(IKnowledgeBaseRepository knowledgeBaseRepository, IOptions<ChatOptions> options)
    {
        _knowledgeBaseRepository = knowledgeBaseRepository;
        _options = options?.Value ?? new ChatOptions();
    }

    public async Task<ChatCompletionRequest> CreateAsync(string message, bool stream, CancellationToken cancellationToken = default)
    {
        var knowledgeBase = await _knowledgeBaseRepository.GetKnowledgeBaseAsync();
        var systemContent = BuildSystemContent(knowledgeBase);

        return new ChatCompletionRequest(
            _options.Model,
            [
                new ChatMessage("system", systemContent),
                new ChatMessage("user", message)
            ],
            stream,
            new ChatGenerationOptions(
                _options.Temperature,
                _options.TopP,
                _options.TopK,
                _options.RepeatPenalty,
                _options.NumPredict,
                _options.NumCtx));
    }

    private static string BuildSystemContent(string knowledgeBase)
    {
        var systemContent = new StringBuilder();
        systemContent.Append(SystemPromptTemplate);

        if (!string.IsNullOrWhiteSpace(knowledgeBase))
        {
            systemContent.AppendLine();
            systemContent.AppendLine();
            systemContent.AppendLine("---");
            systemContent.AppendLine(knowledgeBase);
            systemContent.Append("---");
        }

        return systemContent.ToString();
    }
}
