using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GigaChatIntegration
{
    internal class Program
    {
        static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
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


            while (true)
            {
                Console.Write("Твое сообщение:");
                var userInput = Console.ReadLine();

                if (userInput == "выход")
                    break;

                messages.Add(new ChatMessage("user", userInput));

                var answer = AskGigaChat(messages, accessToken);

                messages.Add(new ChatMessage("assistant", answer));
                Console.WriteLine($"GigaChat: {answer}");
            }
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

    record ChatMessage(string Role, string Content);

    record ChatRequest(string Model, List<ChatMessage> Messages);

    record ChatResponse(List<Choice> Choices);
    record Choice(ChatMessage Message);
}
