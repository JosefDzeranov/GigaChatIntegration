using System.Text.Json;

namespace GigaChatIntegration.GigaChat.Models
{
    // Вызов функции от модели: имя + аргументы. У GigaChat arguments — JSON-ОБЪЕКТ (не строка).
    internal record FunctionCall(string Name, JsonElement Arguments);

    // Описание функции для модели: имя, что делает, схема параметров (JSON Schema).
    internal record FunctionDef(string Name, string Description, object Parameters);
}
