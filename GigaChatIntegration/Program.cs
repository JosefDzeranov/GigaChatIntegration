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

        // ── БАЗА ЗНАНИЙ ──────────────────────────────────────────────────────────
        //  Наши «документы» — короткие заметки по C#. На Дне 4 заменим их на реальные
        //  файлы (и научимся резать длинные документы на куски-«чанки»). Сегодня держим
        //  прямо в коде, чтобы сфокусироваться на сути — поиске по смыслу.
        private static readonly KnowledgeDoc[] Knowledge =
        {
            new("Значимые и ссылочные типы",
                "struct — значимый (value) тип: при присваивании копируется целиком, у каждой переменной свой экземпляр. class — ссылочный: копируется только ссылка на один объект в куче. Поэтому правка копии struct не трогает оригинал, а копии class — трогает."),
            new("Списки и массивы",
                "List<T> — динамическая коллекция: хранит элементы по порядку, умеет Add, Remove, индексатор [i], Count, растёт сама. Массив T[] — фиксированной длины. Когда число элементов заранее неизвестно — берут List<T>."),
            new("Словарь Dictionary",
                "Dictionary<TKey,TValue> хранит пары «ключ → значение» и ищет по ключу почти мгновенно (хеш-таблица). ContainsKey проверяет наличие ключа, TryGetValue безопасно достаёт значение без исключения, если ключа нет."),
            new("Обработка ошибок",
                "Чтобы программа не падала при сбое, опасный код оборачивают в try/catch: в try — действие, которое может бросить исключение, в catch — что делать при ошибке. Блок finally выполняется всегда. Своё исключение бросают через throw new Exception(\"текст\")."),
            new("Асинхронность async/await",
                "async/await не блокирует поток, пока программа чего-то ждёт (ответ из сети, чтение файла). Метод помечают async, «ожидающие» вызовы — await. Такие методы возвращают Task или Task<T>. Это про отзывчивость во время ожидания, а не про скорость вычислений."),
            new("LINQ — запросы к коллекциям",
                "LINQ фильтрует и преобразует коллекции цепочкой методов: Where — отбор по условию, Select — преобразование каждого элемента, OrderBy — сортировка, First/Any/Count — выборка и подсчёт. Работает над любым IEnumerable<T>."),
            new("Проверка на null",
                "Оператор ?? (null-coalescing) возвращает левый операнд, если он не null, иначе правый: name ?? \"гость\". Оператор ?. безопасно обращается к члену: user?.Name вернёт null, если user равен null, вместо падения программы."),
            new("Интерфейсы и полиморфизм",
                "Интерфейс — это контракт: список методов и свойств без реализации. Класс, реализующий интерфейс, обязан их предоставить. Благодаря этому код работает с интерфейсом, не зная конкретный класс, — это и есть полиморфизм."),
            new("Строки и интерполяция",
                "Строки в C# неизменяемы. Удобная склейка — интерполяция: $\"Привет, {name}!\". Полезное: string.IsNullOrWhiteSpace, Trim, Split, Contains, ToLower. Когда собираешь строку из многих кусков в цикле — бери StringBuilder."),
            new("Целочисленное деление",
                "Деление двух int даёт int — дробная часть отбрасывается: 7 / 2 == 3. Чтобы получить 3.5, хотя бы один операнд должен быть double: 7 / 2.0. Остаток от деления даёт оператор %: 7 % 2 == 1."),
        };

        static void Main(string[] args)
        {
            var authKey = "MDE5ZWYwMjQtZjhkNi03ZmI5LTlkNDktYWQ4MmJiODQ5OTRhOjllMGQ2MWJiLTVmODktNDE1ZC04Y2JhLTRmNDQzM2Q5OGFkNw==";

            var accessToken = GetAccessToken(authKey);

            // (1) ИНДЕКСАЦИЯ. Один раз прогоняем все заметки через эмбеддинги и запоминаем
            //     пары {заметка, вектор}. Это наше «хранилище» для поиска. Делаем ОДНИМ
            //     батч-запросом — список текстов разом (один поход в сеть на всю базу).
            Console.WriteLine($"Индексирую базу знаний ({Knowledge.Length} заметок)...");
            float[][] vectors = Embed(Knowledge.Select(d => $"{d.Title}. {d.Text}").ToList(), accessToken);

            var knowledgeIndexes = new List<Indexed>();
            for (int i = 0; i < Knowledge.Length; i++)
                knowledgeIndexes.Add(new Indexed(Knowledge[i], vectors[i]));
            Console.WriteLine($"Готово: {knowledgeIndexes.Count} заметок, размерность вектора смысла — {vectors[0].Length}.\n");

            Console.WriteLine("=== Поиск по смыслу в базе знаний по C# ===");
            Console.WriteLine("Спроси своими словами — найду по смыслу, не по совпадению слов. Например:");
            Console.WriteLine("  • «чем массив отличается от словаря?»");
            Console.WriteLine("  • «как не уронить программу при ошибке?»");
            Console.WriteLine("  • «что подставить, если значения нет?»");
            Console.WriteLine("'выход' — закончить.\n");

            while (true)
            {
                Console.Write("Вопрос: ");
                string? question = Console.ReadLine();

                if (question == "выход") break;

                // (2) ПОИСК. Вектор вопроса → косинус ко всем заметкам → топ-3 по смыслу.
                List<Scored> top = Search(question, knowledgeIndexes, topK: 3, accessToken);

                Console.WriteLine("\n  Нашёл по смыслу (близость 0..1):");
                foreach (var s in top)
                    Console.WriteLine($"    [{s.Score:0.00}] {s.Doc.Title}");

                // (3) ФИНАЛ-МОСТИК (RAG): отдаём найденные заметки в GigaChat как контекст —
                //     он отвечает СТРОГО по ним. Целиком соберём на Дне 4.
                string answer = AskWithContext(question, top, accessToken);
                Console.WriteLine($"\nНаставник: {answer}\n");
            }


            //Console.WriteLine("Готово!\n");
            //Console.WriteLine("=== ИИ-наставник по C#: ведёт план изучения и сам проверяет тестами ===");
            //Console.WriteLine("Примеры:");
            //Console.WriteLine("  • «хочу разобраться с делегатами, это важно»  (добавит в план)");
            //Console.WriteLine("  • «что у меня в плане?»                        (покажет план)");
            //Console.WriteLine("  • «проверь меня по разнице struct и class»     (устроит мини-тест)");
            //Console.WriteLine("  • «я разобрался с делегатами»                  (отметит изученным)");
            //Console.WriteLine("'выход' — закончить.\n");

            //// Системный промпт — «характер и правила» наставника (День 2: системные промпты).
            //var history = new List<ChatMessage>
            //{
            //    new("system",
            //        "Ты — помощник-наставник по обучению C# для начинающего разработчика. " +
            //        "Ты ведёшь его личный план изучения тем и помогаешь проверять знания. " +
            //        "У тебя есть инструменты (функции), которыми ты управляешь сам:\n" +
            //        "• add_topic — когда ученик хочет что-то изучить, просит добавить тему, " +
            //        "или ты сам по ходу разговора считаешь тему важной.\n" +
            //        "• list_topics — когда ученик спрашивает, что у него в плане, что осталось или с чего начать.\n" +
            //        "• mark_studied — когда ученик говорит, что разобрался с темой, прошёл её или выучил.\n" +
            //        "• quiz_me — когда ученик просит проверить знания («проверь меня», «дай тест», " +
            //        "«я готов по теме X») ИЛИ когда он уверяет, что разобрался, и это стоит подтвердить мини-тестом.\n" +
            //        "Правила:\n" +
            //        "- Вызывай функцию ТОЛЬКО когда она действительно нужна. За один ответ — не больше одного вызова функции.\n" +
            //        "- Не придумывай сам текст тест-вопроса и варианты — их ВСЕГДА формирует функция quiz_me. " +
            //        "Ты лишь объявляешь о начале проверки и комментируешь её результат.\n" +
            //        "- Когда пришёл результат quiz_me: если ответ верный — кратко похвали и предложи отметить тему " +
            //        "изученной (mark_studied) или добавить смежную; если неверный — мягко разбери ошибку одним " +
            //        "предложением и предложи оставить тему на повтор.\n" +
            //        "- Никогда не выдумывай результат функции — дождись, что вернёт программа.\n" +
            //        "- Если функции не нужны — просто ответь словами. Отвечай кратко, доброжелательно и на русском."),
            //};


            //// Функции, которые мы РАЗРЕШАЕМ модели вызывать (имя + описание + схема аргументов).
            //var functions = new List<FunctionDef>
            //{
            //    new("add_topic",
            //        "Добавляет тему в личный план изучения C#.",
            //        new
            //        {
            //            type = "object",
            //            properties = new
            //            {
            //                title    = new { type = "string", description = "Тема для изучения, напр. «делегаты»" },
            //                priority = new { type = "string", @enum = new[] { "высокий", "средний", "низкий" },
            //                                 description = "Насколько важно изучить тему" },
            //                note     = new { type = "string", description = "Заметка/зачем изучать (свободная форма). Может отсутствовать." },
            //            },
            //            required = new[] { "title" },
            //        }),

            //    new("list_topics",
            //        "Возвращает текущий план изучения ученика (что в плане и что уже изучено).",
            //        new { type = "object", properties = new { } }),

            //    new("mark_studied",
            //        "Помечает тему в плане как изученную (когда ученик говорит, что разобрался с ней).",
            //        new
            //        {
            //            type = "object",
            //            properties = new
            //            {
            //                title = new { type = "string", description = "Какую тему из плана отметить изученной" },
            //            },
            //            required = new[] { "title" },
            //        }),

            //    new("quiz_me",
            //        "Проводит мини-тест (1 вопрос с 4 вариантами) по заданной теме C# и проверяет ответ ученика.",
            //        new
            //        {
            //            type = "object",
            //            properties = new
            //            {
            //                topic = new { type = "string", description = "Тема теста, напр. «разница между struct и class»" },
            //            },
            //            required = new[] { "topic" },
            //        }),
            //};

            //while (true)
            //{
            //    Console.Write("Твое сообщение:");
            //    var userInput = Console.ReadLine();

            //    if (userInput == "выход")
            //        break;

            //    history.Add(new ChatMessage("user", userInput));

            //    var reply = AskGigaChat(history, accessToken, functions);

            //    string answer;
            //    if (reply.FunctionCall is not null)
            //    {
            //        // Модель решила вызвать функцию. Сохраняем её «ход» (вместе с
            //        // functions_state_id — GigaChat ждёт его обратно) и выполняем функцию.
            //        history.Add(reply with { Content = reply.Content ?? "" });

            //        string result = ExecuteFunction(reply.FunctionCall, accessToken);

            //        history.Add(new ChatMessage("function", result, Name: reply.FunctionCall.Name));

            //        // Финальный ответ просим УЖЕ БЕЗ функций: модель обязана ответить текстом
            //        // и не зациклится на повторных вызовах одной и той же функции (Function
            //        // Calling у GigaChat в бете это любит — звал бы list_topics по кругу).
            //        answer = AskRaw(history, accessToken);
            //    }
            //    else
            //    {
            //        // Функция не нужна — это обычный текстовый ответ.
            //        answer = reply.Content ?? "";
            //    }


            //    history.Add(new ChatMessage("assistant", answer));
            //    Console.WriteLine($"GigaChat: {answer}");
            //}
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  ФИНАЛ-МОСТИК В ДЕНЬ 4 (RAG): ответ СТРОГО по найденным заметкам.
        //  Складываем найденное в контекст и системным промптом велим модели не
        //  выдумывать. Так «поиск по смыслу» превращается в ответ ПО ДОКУМЕНТАМ.
        // ─────────────────────────────────────────────────────────────────────────
        private static string AskWithContext(string question, List<Scored> context, string accessToken)
        {
            var sb = new StringBuilder();
            foreach (var s in context)
                sb.AppendLine($"### {s.Doc.Title}\n{s.Doc.Text}\n");

            var messages = new List<ChatMessage>
            {
                new("system",
                    "Ты — помощник по C#. Отвечай на вопрос ТОЛЬКО на основе заметок ниже. " +
                    "Если ответа в них нет — честно скажи, что в базе знаний этого нет, и НЕ выдумывай. " +
                    "Отвечай кратко и по-русски; в конце укажи, на какую заметку опираешься."),
                new("user", $"ЗАМЕТКИ:\n{sb}\nВОПРОС: {question}"),
            };

            return AskRaw(messages, accessToken);
        }

        // ПОИСК ПО СМЫСЛУ: эмбеддим вопрос, считаем косинус к каждой заметке,
        // сортируем по убыванию близости и берём top-K самых похожих.
        private static List<Scored> Search(string query, List<Indexed> index, int topK, string accessToken)
        {
            float[] queryVector = Embed(new List<string> { query }, accessToken)[0];

            return index
                .Select(item => new Scored(item.Doc, Cosine(queryVector, item.Vector)))
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();
        }

        // КОСИНУСНАЯ БЛИЗОСТЬ двух векторов: ~1 — смысл совпадает, ~0 — не связаны.
        // Это косинус угла между векторами = (a·b) / (|a|·|b|). Длину векторов формула
        // учитывает сама, поэтому заранее нормировать не нужно.
        private static double Cosine(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];   // скалярное произведение
                na += a[i] * a[i];   // квадрат длины a
                nb += b[i] * b[i];   // квадрат длины b
            }
            // + 1e-9 — крошечная добавка, чтобы случайно не делить на ноль.
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  ЭМБЕДДИНГИ: превращаем тексты в векторы чисел (координаты смысла).
        //  Один запрос принимает СПИСОК строк (input) и возвращает по вектору на каждую.
        // ─────────────────────────────────────────────────────────────────────────
        private static float[][] Embed(List<string> texts, string accessToken)
        {
            var body = new EmbeddingRequest("Embeddings", texts);
            string json = JsonSerializer.Serialize(body, JsonOpts);

            const string EmbeddingsUrl = "https://gigachat.devices.sberbank.ru/api/v1/embeddings";
            using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingsUrl)
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

            var result = JsonSerializer.Deserialize<EmbeddingResponse>(ReadBody(response), JsonOpts)!;

            // У каждого вектора index = позиция текста во входном списке. Сортируем по index,
            // чтобы порядок векторов ТОЧНО совпал с порядком входных текстов.
            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToArray();
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
        private static string ExecuteFunction(FunctionCall call, string accessToken)
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
                case "quiz_me":
                    {
                        string topic = GetStr(call.Arguments, "topic") ?? "C#";
                        Console.WriteLine($"\n  [запускаю тест по теме: {topic}]");

                        // (1) Структурированный вывод как ДВИЖОК инструмента: отдельный запрос
                        //     к модели за строгим JSON по схеме QuizQuestion (+ StripJsonFences).
                        //     Модель иногда присылает кривой JSON (хвостовая запятая, лишний текст) —
                        //     оборачиваем в try/catch, чтобы один тест не уронил весь чат.
                        QuizQuestion quiz;
                        try
                        {
                            quiz = GenerateQuiz(topic, accessToken);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  [не удалось собрать тест: {ex.Message}]\n");
                            return JsonSerializer.Serialize(
                                new { topic, error = "Не удалось сгенерировать корректный тест. Предложи попробовать ещё раз." }, JsonOpts);
                        }

                        // Подстраховка от кривого ответа: нет вариантов / индекс вне диапазона.
                        if (quiz.Options is not { Length: > 0 })
                        {
                            Console.WriteLine("  [тест пришёл без вариантов ответа]\n");
                            return JsonSerializer.Serialize(
                                new { topic, error = "Тест без вариантов. Предложи попробовать ещё раз." }, JsonOpts);
                        }
                        int correctIndex = quiz.CorrectIndex >= 0 && quiz.CorrectIndex < quiz.Options.Length
                            ? quiz.CorrectIndex : 0;

                        // Источник вопроса — живая генерация (структурированный вывод по схеме).
                        Console.WriteLine("  [вопрос сгенерирован вживую — структурированный вывод по схеме]");

                        // (2) Показываем тест ученику.
                        Console.WriteLine($"❓ {quiz.Question}");
                        for (int i = 0; i < quiz.Options.Length; i++)
                            Console.WriteLine($"    {i + 1}. {quiz.Options[i]}");

                        // (3) Читаем ответ ученика (блокирующий ReadLine прямо в обработчике —
                        //     учебное упрощение: формально мы всё ещё «выполняем функцию»).
                        Console.Write("Твой ответ (1-4): ");
                        bool parsed = int.TryParse(Console.ReadLine(), out int num);
                        bool correct = parsed && num - 1 == correctIndex;

                        // (4) Мгновенный фидбэк ученику.
                        Console.WriteLine(correct ? "✅ Верно!" : "❌ Неверно.");
                        Console.WriteLine($"   Разбор: {quiz.Explanation}\n");

                        // (5) Возвращаем модели структурный вердикт — она прокомментирует
                        //     и сможет предложить mark_studied / add_topic.
                        return JsonSerializer.Serialize(new
                        {
                            topic,
                            question = quiz.Question,
                            userAnswer = parsed ? num : (int?)null,
                            correct,
                            correctOption = quiz.Options[correctIndex],
                            explanation = quiz.Explanation,
                        }, JsonOpts);
                    }

                default:
                    // Модель попросила функцию, которой у нас нет — честно говорим об этом.
                    return JsonSerializer.Serialize(new { error = $"Неизвестная функция: {call.Name}" }, JsonOpts);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  СТРУКТУРИРОВАННЫЙ ВЫВОД: просим модель вернуть строгий JSON (тест-вопрос)
        //  и разбираем его в объект C#. Это ДВИЖОК инструмента quiz_me.
        //  Живая генерация — единственный источник вопроса. Структурированный вывод
        //  гарантирует ФОРМУ (4 варианта + индекс верного), но НЕ правильность фактов:
        //  по слабым для модели темам вопрос может выйти с шероховатостями — для демо ок.
        // ─────────────────────────────────────────────────────────────────────────
        private static QuizQuestion GenerateQuiz(string topic, string accessToken)
        {

            // Системный промпт генератора теста: держим модель в её зоне — факты языка C#
            // (ключевые слова, типы, синтаксис, операторы, коллекции, строки, ООП) — и
            // задаём строгую JSON-схему ответа. Это и есть «управление моделью» из Дня 2.
            const string GeneratorSystemPrompt =
                "Ты — генератор тест-вопросов про язык C# для начинающих. " +
                "Спрашивай про факты самого языка и базовой библиотеки .NET: ключевые слова (var, const, readonly, static, ref, out), " +
                "типы и их различия (struct и class, значимые и ссылочные, nullable), синтаксис, поведение операторов, " +
                "базовые коллекции (List<T>, Dictionary<TKey,TValue>), строки, исключения, ООП в C#. " +
                "Сначала ВЫБЕРИ один факт, в котором уверен, сделай его верным ответом, затем придумай 3 правдоподобных, но неверных. " +
                "Вопрос должен быть КОНКРЕТНЫМ (про конкретное ключевое слово, тип или метод), а не общим рассуждением. " +
                "Верни ТОЛЬКО JSON-объект по схеме:\n" +
                "{ \"question\": строка, \"options\": [ровно 4 строки], \"correctIndex\": число 0..3, \"explanation\": строка }\n" +
                "correctIndex — позиция верного варианта в options, нумерация с НУЛЯ. explanation объясняет именно options[correctIndex]. " +
                "Ровно один верный вариант, остальные три неверны. Без markdown, без текста до или после JSON.";



            var messages = new List<ChatMessage>
            {
                new("system", GeneratorSystemPrompt),
                // FEW-SHOT: один образец задаёт И форму JSON, И стиль (конкретный факт языка).
                //new("user", "Тема для теста (про язык C#): ключевое слово var"),
                //new("assistant",
                //    "{\"question\":\"Что делает ключевое слово var при объявлении локальной переменной в C#?\"," +
                //    "\"options\":[\"Тип выводится компилятором из выражения справа\"," +
                //    "\"Создаёт переменную без типа, как в JavaScript\"," +
                //    "\"Объявляет переменную, которую нельзя переприсвоить\"," +
                //    "\"Делает переменную видимой во всех методах класса\"]," +
                //    "\"correctIndex\":0,\"explanation\":\"var — это неявная типизация: компилятор сам выводит конкретный тип из инициализатора, переменная остаётся строго типизированной.\"}"),
                new("user", $"Тема для теста (про язык C#): {topic}"),
            };

            // Низкая температура: строгому вопросу нужна предсказуемость, а не фантазия.
            // Иногда модель оборачивает JSON в ```json ... ``` — срежем «забор».
            //string clean = StripJsonFences(AskRaw(messages, temperature: 0.2));
            string clean = AskRaw(messages, accessToken, temperature: 0.2);

            // Кривой JSON бросит из Deserialize, пустой ("null") — поймаем через ??.
            // Любую ошибку перехватит обработчик quiz_me и попросит модель повторить.
            return JsonSerializer.Deserialize<QuizQuestion>(clean, JsonOpts)
                   ?? throw new InvalidOperationException("Модель вернула пустой JSON вместо тест-вопроса.");
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

    // Тест-вопрос, который достаём структурированным выводом в GenerateQuiz
    // (движок инструмента quiz_me).
    record QuizQuestion(string Question, string[] Options, int CorrectIndex, string Explanation);



    // ── Эмбеддинги ───────────────────────────────────────────────────────────────
    // Запрос: имя модели эмбеддингов + список текстов (input принимает массив строк).
    record EmbeddingRequest(string Model, List<string> Input);
    // Ответ: список векторов; у каждого Embedding — числа, Index — позиция текста во входе.
    record EmbeddingResponse(List<EmbeddingData> Data);
    record EmbeddingData(float[] Embedding, int Index);

    // Заметка базы знаний («документ»). На Дне 4 источник заменим на реальные файлы.
    record KnowledgeDoc(string Title, string Text);

    // Проиндексированная заметка: сам документ + его вектор смысла.
    record Indexed(KnowledgeDoc Doc, float[] Vector);

    record Scored(KnowledgeDoc Doc, double Score);
}
