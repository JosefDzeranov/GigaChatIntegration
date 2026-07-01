using System.Text.Json.Serialization;

namespace GigaChatIntegration.GigaChat.Models
{
    // Сообщение переписки. Кроме role/content может нести:
    //   • function_call — когда модель (assistant) решила вызвать функцию;
    //   • functions_state_id — служебный id, который GigaChat просит вернуть обратно;
    //   • name — имя функции, когда мы отправляем РЕЗУЛЬТАТ (role = "function").
    internal record ChatMessage(
        string Role,
        string? Content,
        [property: JsonPropertyName("function_call")] FunctionCall? FunctionCall = null,
        [property: JsonPropertyName("functions_state_id")] string? FunctionsStateId = null,
        string? Name = null);

    internal record ChatRequest(
        string Model,
        List<ChatMessage> Messages,
        List<FunctionDef>? Functions = null,
        [property: JsonPropertyName("function_call")] string? FunctionCallMode = null);

    internal record ChatResponse(List<Choice> Choices);

    internal record Choice(
       ChatMessage Message,
       [property: JsonPropertyName("finish_reason")] string? FinishReason = null);
}
