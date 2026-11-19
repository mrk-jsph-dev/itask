using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ITask.Api.Infrastructure.Ai;

/// <summary>
/// Calls the OpenAI model deployment in Microsoft Foundry with the API's managed identity
/// (role "Cognitive Services OpenAI User"); no keys.
/// </summary>
internal sealed class AzureOpenAiService : IAiService
{
    private readonly ChatClient _chat;

    public AzureOpenAiService(IOptions<AiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        var client = new AzureOpenAIClient(settings.Endpoint!, new DefaultAzureCredential());
        _chat = client.GetChatClient(settings.Deployment);
    }

    public bool IsEnabled => true;

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens };
        if (request.JsonSchema is not null)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                request.JsonSchemaName ?? "answer", request.JsonSchema, jsonSchemaIsStrict: true);
        }

        ClientResult<ChatCompletion> result = await _chat.CompleteChatAsync(
            [new SystemChatMessage(request.System), new UserChatMessage(request.User)], options, ct);
        var completion = result.Value;
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : "";
        return new AiResult(text, completion.Usage?.InputTokenCount ?? 0, completion.Usage?.OutputTokenCount ?? 0);
    }
}
