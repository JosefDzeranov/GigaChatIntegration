using GigaChatIntegration.GigaChat;
using GigaChatIntegration.GigaChat.Models;
using System.Text.Json;

namespace GigaChatIntegration
{
    internal class DocAssistant
    {
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        private readonly GigaChatClient _gigaChatClient;
        private readonly KnowledgeBase _knowledgeBase;

        // Память диалога. Начинается с системного промпта (День 2) — правил, когда искать.
        // В веб-версии историю держали бы НА ПОЛЬЗОВАТЕЛЯ/СЕССИЮ (запросов много, они параллельны).
        private readonly List<ChatMessage> history = new()
        {
            new("system",
                "Ты — помощник по документам. Если вопрос про содержание документов — ВЫЗОВИ функцию " +
                "search_documents и ответь ТОЛЬКО по найденным фрагментам, укажи файл-источник. Если в " +
                "найденном ответа нет — честно скажи, что в документах этого нет, и не выдумывай. На " +
                "приветствия и общие вопросы отвечай сам, без поиска. Отвечай кратко и по-русски."),
        };

        // Функция, которую РАЗРЕШАЕМ модели вызывать (Function Calling, День 2). Схема
        // параметров = JSON Schema: модель пришлёт аргумент query нужной структурой.
        private static readonly List<FunctionDef> Functions = new()
        {
            new("search_documents",
                "Ищет ответ в документах пользователя по смыслу. Вызывай, когда вопрос про " +
                "содержание документов (тарифы, доступ, сертификат, оплата, возврат, поддержка и т.п.).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Что искать — суть вопроса своими словами" },
                    },
                    required = new[] { "query" },
                }),
        };

        public DocAssistant(GigaChatClient gc, KnowledgeBase kb)
        {
            this._gigaChatClient = gc;
            this._knowledgeBase = kb;
        }

        // Задать вопрос. Модель сама решает: искать в документах или ответить сразу.
        // Возвращаем ответ + источники (пустой список, если поиск не понадобился).
        // onSearch — необязательный хук для UI: консоль печатает «ищу…», веб мог бы залогировать.
        public (string Answer, List<Scored> Sources) Ask(string question, Action<string>? onSearch = null)
        {
            history.Add(new ChatMessage("user", question));

            // Спрашиваем модель С функциями — она решит: вызвать search_documents или ответить сама.
            ChatMessage reply = _gigaChatClient.ChatWithFunctions(history, Functions);

            if (reply.FunctionCall is { Name: "search_documents" })
            {
                // Модель решила искать. Сохраняем её «ход» (с functions_state_id) и выполняем поиск.
                history.Add(reply with { Content = reply.Content ?? "" });
                string query = GetStr(reply.FunctionCall.Arguments, "query") ?? question;
                onSearch?.Invoke(query);

                // (1) retrieval — находим по смыслу (День 3).
                List<Scored> top = _knowledgeBase.Search(query, _gigaChatClient, topK: 3);

                // (2) возвращаем найденные куски модели как результат функции.
                string result = JsonSerializer.Serialize(new
                {
                    results = top.Select(s => new { source = s.Chunk.Source, text = s.Chunk.Text }),
                }, JsonOpts);
                history.Add(new ChatMessage("function", result, Name: "search_documents"));

                // (3) generation — финальный ответ УЖЕ БЕЗ функций (модель не зациклится на вызовах).
                string answer = _gigaChatClient.Chat(history);
                history.Add(new ChatMessage("assistant", answer));
                return (answer, top);
            }

            // Модель решила ответить сама (приветствие/общий вопрос) — без поиска.
            string plain = reply.Content ?? "";
            history.Add(new ChatMessage("assistant", plain));
            return (plain, new List<Scored>());
        }

        // Читает строковое поле из аргументов функции (у GigaChat arguments — объект-JsonElement).
        private static string? GetStr(JsonElement obj, string field)
            => obj.ValueKind == JsonValueKind.Object
               && obj.TryGetProperty(field, out var v)
               && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
    }
}