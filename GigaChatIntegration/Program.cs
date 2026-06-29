using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GigaChatIntegration
{
    internal class Program
    {
        static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
        private static readonly List<StudyTopic> plan = new();
        static void Main(string[] args)
        {
            var authKey = "MDE5ZWYwMjQtZjhkNi03ZmI5LTlkNDktYWQ4MmJiODQ5OTRhOmQ0YTYzOTg5LTIwZmQtNGJmNy05YTcyLTQ3NjllMzQwMjVhMQ==";

            var accessToken = GetAccessToken(authKey);

            Console.WriteLine("Готово!\n");
            Console.WriteLine("=== ИИ-наставник по C#: ведёт план изучения и сам проверяет тестами ===");
            Console.WriteLine("Примеры:");
            Console.WriteLine("  • «хочу разобраться с делегатами, это важно»  (добавит в план)");
            Console.WriteLine("  • «что у меня в плане?»                        (покажет план)");
            Console.WriteLine("  • «проверь меня по разнице struct и class»     (устроит мини-тест)");
            Console.WriteLine("  • «я разобрался с делегатами»                  (отметит изученным)");
            Console.WriteLine("'выход' — закончить.\n");

            // Системный промпт — «характер и правила» наставника (День 2: системные промпты).
            var history = new List<ChatMessage>
            {
                new("system",
                    "Ты — помощник-наставник по обучению C# для начинающего разработчика. " +
                    "Ты ведёшь его личный план изучения тем и помогаешь проверять знания. " +
                    "У тебя есть инструменты (функции), которыми ты управляешь сам:\n" +
                    "• add_topic — когда ученик хочет что-то изучить, просит добавить тему, " +
                    "или ты сам по ходу разговора считаешь тему важной.\n" +
                    "• list_topics — когда ученик спрашивает, что у него в плане, что осталось или с чего начать.\n" +
                    "• mark_studied — когда ученик говорит, что разобрался с темой, прошёл её или выучил.\n" +
                    "• quiz_me — когда ученик просит проверить знания («проверь меня», «дай тест», " +
                    "«я готов по теме X») ИЛИ когда он уверяет, что разобрался, и это стоит подтвердить мини-тестом.\n" +
                    "Правила:\n" +
                    "- Вызывай функцию ТОЛЬКО когда она действительно нужна. За один ответ — не больше одного вызова функции.\n" +
                    "- Не придумывай сам текст тест-вопроса и варианты — их ВСЕГДА формирует функция quiz_me. " +
                    "Ты лишь объявляешь о начале проверки и комментируешь её результат.\n" +
                    "- Когда пришёл результат quiz_me: если ответ верный — кратко похвали и предложи отметить тему " +
                    "изученной (mark_studied) или добавить смежную; если неверный — мягко разбери ошибку одним " +
                    "предложением и предложи оставить тему на повтор.\n" +
                    "- Никогда не выдумывай результат функции — дождись, что вернёт программа.\n" +
                    "- Если функции не нужны — просто ответь словами. Отвечай кратко, доброжелательно и на русском."),
            };


            // Функции, которые мы РАЗРЕШАЕМ модели вызывать (имя + описание + схема аргументов).
            var functions = new List<FunctionDef>
            {
                new("add_topic",
                    "Добавляет тему в личный план изучения C#.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            title    = new { type = "string", description = "Тема для изучения, напр. «делегаты»" },
                            priority = new { type = "string", @enum = new[] { "высокий", "средний", "низкий" },
                                             description = "Насколько важно изучить тему" },
                            note     = new { type = "string", description = "Заметка/зачем изучать (свободная форма). Может отсутствовать." },
                        },
                        required = new[] { "title" },
                    }),

                new("list_topics",
                    "Возвращает текущий план изучения ученика (что в плане и что уже изучено).",
                    new { type = "object", properties = new { } }),

                new("mark_studied",
                    "Помечает тему в плане как изученную (когда ученик говорит, что разобрался с ней).",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string", description = "Какую тему из плана отметить изученной" },
                        },
                        required = new[] { "title" },
                    }),

                new("quiz_me",
                    "Проводит мини-тест (1 вопрос с 4 вариантами) по заданной теме C# и проверяет ответ ученика.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            topic = new { type = "string", description = "Тема теста, напр. «разница между struct и class»" },
                        },
                        required = new[] { "topic" },
                    }),
            };

            while (true)
            {
                Console.Write("Твое сообщение:");
                var userInput = Console.ReadLine();

                if (userInput == "выход")
                    break;

                history.Add(new ChatMessage("user", userInput));

                var reply = AskGigaChat(history, accessToken, functions);

                string answer;
                if (reply.FunctionCall is not null)
                {
                    // Модель решила вызвать функцию. Сохраняем её «ход» (вместе с
                    // functions_state_id — GigaChat ждёт его обратно) и выполняем функцию.
                    history.Add(reply with { Content = reply.Content ?? "" });

                    string result = ExecuteFunction(reply.FunctionCall);

                    history.Add(new ChatMessage("function", result, Name: reply.FunctionCall.Name));

                    // Финальный ответ просим УЖЕ БЕЗ функций: модель обязана ответить текстом
                    // и не зациклится на повторных вызовах одной и той же функции (Function
                    // Calling у GigaChat в бете это любит — звал бы list_topics по кругу).
                    answer = AskRaw(history, accessToken);
                }
                else
                {
                    // Функция не нужна — это обычный текстовый ответ.
                    answer = reply.Content ?? "";
                }


                history.Add(new ChatMessage("assistant", answer));
                Console.WriteLine($"GigaChat: {answer}");
            }
        }

        // Простой запрос БЕЗ функций — возвращает текст. Используется и в GenerateQuiz
        // (с низкой температурой — нужен предсказуемый JSON), и для ФИНАЛЬНОГО ответа
        // после вызова функции (без функций модель не зациклится).
        private static string AskRaw(List<ChatMessage> messages, string accessToken, double? temperature = null)
        {
            var body = new ChatRequest("GigaChat", messages, Temperature: temperature);
            ChatResponse result = PostChat(body, accessToken);
            return result.Choices[0].Message.Content ?? "";
        }



        // ─────────────────────────────────────────────────────────────────────────
        //  ВЫПОЛНЕНИЕ ФУНКЦИЙ, которые захотела вызвать модель.
        //  Возвращаем результат JSON-строкой — её увидит модель.
        // ─────────────────────────────────────────────────────────────────────────
        private static string ExecuteFunction(FunctionCall call)
        {
            switch (call.Name)
            {
                case "add_topic":
                    {
                        // Аргументы у GigaChat приходят ОБЪЕКТОМ — читаем поля из JsonElement.
                        JsonElement a = call.Arguments;
                        string title = GetStr(a, "title") ?? "(без названия)";
                        string priority = GetStr(a, "priority") ?? "средний";
                        string? note = GetStr(a, "note");

                        plan.Add(new StudyTopic(title, priority, note));
                        Console.WriteLine($"  [добавил в план: {title}]");
                        return JsonSerializer.Serialize(new { status = "ok", added = title, total = plan.Count }, JsonOpts);
                    }

                case "list_topics":
                    Console.WriteLine("  [показываю план]");
                    return JsonSerializer.Serialize(
                        new { topics = plan, total = plan.Count, studied = plan.Count(t => t.Studied) }, JsonOpts);

                case "mark_studied":
                    {
                        string title = GetStr(call.Arguments, "title") ?? "";
                        // Модель могла слегка переформулировать тему — ищем по вхождению без учёта регистра.
                        int idx = plan.FindIndex(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            plan[idx] = plan[idx] with { Studied = true };   // record + with: новый экземпляр, Studied=true
                            Console.WriteLine($"  [отметил изученным: {plan[idx].Title}]");
                        }
                        return JsonSerializer.Serialize(
                            new { status = idx >= 0 ? "ok" : "not_found", title, studied = idx >= 0 }, JsonOpts);
                    }

                // ── ТОЧКА СОЕДИНЕНИЯ ДВУХ ТЕХНИК ──────────────────────────────────
                //  Function Calling решил ЗАПУСТИТЬ тест (quiz_me), а форму вопроса
                //  гарантирует СТРУКТУРИРОВАННЫЙ ВЫВОД: внутри зовём GenerateQuiz.
                //case "quiz_me":
                //    {
                //        string topic = GetStr(call.Arguments, "topic") ?? "C#";
                //        Console.WriteLine($"\n  [запускаю тест по теме: {topic}]");

                //        // (1) Структурированный вывод как ДВИЖОК инструмента: отдельный запрос
                //        //     к модели за строгим JSON по схеме QuizQuestion (+ StripJsonFences).
                //        //     Модель иногда присылает кривой JSON (хвостовая запятая, лишний текст) —
                //        //     оборачиваем в try/catch, чтобы один тест не уронил весь чат.
                //        QuizQuestion quiz;
                //        try
                //        {
                //            quiz = GenerateQuiz(topic);
                //        }
                //        catch (Exception ex)
                //        {
                //            Console.WriteLine($"  [не удалось собрать тест: {ex.Message}]\n");
                //            return JsonSerializer.Serialize(
                //                new { topic, error = "Не удалось сгенерировать корректный тест. Предложи попробовать ещё раз." }, JsonOpts);
                //        }

                //        // Подстраховка от кривого ответа: нет вариантов / индекс вне диапазона.
                //        if (quiz.Options is not { Length: > 0 })
                //        {
                //            Console.WriteLine("  [тест пришёл без вариантов ответа]\n");
                //            return JsonSerializer.Serialize(
                //                new { topic, error = "Тест без вариантов. Предложи попробовать ещё раз." }, JsonOpts);
                //        }
                //        int correctIndex = quiz.CorrectIndex >= 0 && quiz.CorrectIndex < quiz.Options.Length
                //            ? quiz.CorrectIndex : 0;

                //        // Источник вопроса — живая генерация (структурированный вывод по схеме).
                //        Console.WriteLine("  [вопрос сгенерирован вживую — структурированный вывод по схеме]");

                //        // (2) Показываем тест ученику.
                //        Console.WriteLine($"❓ {quiz.Question}");
                //        for (int i = 0; i < quiz.Options.Length; i++)
                //            Console.WriteLine($"    {i + 1}. {quiz.Options[i]}");

                //        // (3) Читаем ответ ученика (блокирующий ReadLine прямо в обработчике —
                //        //     учебное упрощение: формально мы всё ещё «выполняем функцию»).
                //        Console.Write("Твой ответ (1-4): ");
                //        bool parsed = int.TryParse(Console.ReadLine(), out int num);
                //        bool correct = parsed && num - 1 == correctIndex;

                //        // (4) Мгновенный фидбэк ученику.
                //        Console.WriteLine(correct ? "✅ Верно!" : "❌ Неверно.");
                //        Console.WriteLine($"   Разбор: {quiz.Explanation}\n");

                //        // (5) Возвращаем модели структурный вердикт — она прокомментирует
                //        //     и сможет предложить mark_studied / add_topic.
                //        return JsonSerializer.Serialize(new
                //        {
                //            topic,
                //            question = quiz.Question,
                //            userAnswer = parsed ? num : (int?)null,
                //            correct,
                //            correctOption = quiz.Options[correctIndex],
                //            explanation = quiz.Explanation,
                //        }, JsonOpts);
                //    }

                default:
                    // Модель попросила функцию, которой у нас нет — честно говорим об этом.
                    return JsonSerializer.Serialize(new { error = $"Неизвестная функция: {call.Name}" }, JsonOpts);
            }
        }

        private static string? GetStr(JsonElement obj, string field)
        {
            return obj.ValueKind == JsonValueKind.Object
                   && obj.TryGetProperty(field, out var v)
                   && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;
        }

        static ChatMessage AskGigaChat(List<ChatMessage> history, string accessToken, List<FunctionDef> functions)
        {
            var body = new ChatRequest("GigaChat", history, functions, FunctionCallMode: "auto");

            ChatResponse result = PostChat(body, accessToken);

            return result!.Choices[0].Message;
        }

        // Отправляет тело в /chat/completions и разбирает ответ.
        private static ChatResponse PostChat(ChatRequest body, string accessToken)
        {
            string json = JsonSerializer.Serialize(body, JsonOpts);

            string chatUrl = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            HttpClient httpClient = new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = httpClient.Send(request);
            response.EnsureSuccessStatusCode();

            return JsonSerializer.Deserialize<ChatResponse>(ReadBody(response), JsonOpts)!;
        }


        static string AskGigaChat(List<ChatMessage> history, string accessToken)
        {
            string json = JsonSerializer.Serialize(new ChatRequest("GigaChat", history), JsonOpts);

            string chatUrl = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            HttpClient httpClient = new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = httpClient.Send(request);
            response.EnsureSuccessStatusCode();

            var result = JsonSerializer.Deserialize<ChatResponse>(ReadBody(response), JsonOpts);

            return result!.Choices[0].Message.Content;
        }

        static string GetAccessToken(string authKey)
        {
            string authUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";

            using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authKey);
            request.Headers.Add("RqUID", Guid.NewGuid().ToString());

            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["scope"] = "GIGACHAT_API_PERS",
            });

            HttpClient httpClient = new(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

            using var response = httpClient.Send(request);
            response.EnsureSuccessStatusCode();

            var token = JsonSerializer.Deserialize<TokenResponse>(ReadBody(response), JsonOpts);

            return token.AccessToken;
        }

        static string ReadBody(HttpResponseMessage response)
        {
            using var stream = response.Content.ReadAsStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
    record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt);

    // Сообщение в переписке. Кроме role/content может нести:
    //   • function_call — когда модель (assistant) решила вызвать функцию;
    //   • functions_state_id — служебный id, который GigaChat просит вернуть обратно;
    //   • name — имя функции, когда мы отправляем РЕЗУЛЬТАТ (role = "function").
    record ChatMessage(
        string Role,
        string? Content,
        [property: JsonPropertyName("function_call")] FunctionCall? FunctionCall = null,
        [property: JsonPropertyName("functions_state_id")] string? FunctionsStateId = null,
        string? Name = null);

    // Вызов функции от модели: имя + аргументы. У GigaChat arguments — это JSON-ОБЪЕКТ,
    // поэтому читаем его универсально как JsonElement (а не как строку, как в OpenAI).
    record FunctionCall(string Name, JsonElement Arguments);

    // Тело запроса к чату: модель + история (+ опц. функции, режим их вызова, температура).
    // Temperature шлём только когда нужна предсказуемость (GenerateQuiz); WhenWritingNull
    // в JsonOpts означает, что для обычных запросов поле не сериализуется (модель берёт дефолт).
    record ChatRequest(
        string Model,
        List<ChatMessage> Messages,
        List<FunctionDef>? Functions = null,
        [property: JsonPropertyName("function_call")] string? FunctionCallMode = null,
        [property: JsonPropertyName("temperature")] double? Temperature = null);



    record ChatResponse(List<Choice> Choices);
    record Choice(ChatMessage Message);

    // Описание функции для модели: имя, что делает, и схема параметров (JSON Schema).
    record FunctionDef(string Name, string Description, object Parameters);

    // Тема в плане изучения. Studied ставит функция mark_studied; изученные темы —
    // задел для Дня 3 (поиск по смыслу «повтори пройденное»).
    record StudyTopic(string Title, string Priority, string? Note, bool Studied = false);
}
