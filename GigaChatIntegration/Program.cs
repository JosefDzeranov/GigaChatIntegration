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
            var authKey = "MDE5ZWYwMjQtZjhkNi03ZmI5LTlkNDktYWQ4MmJiODQ5OTRhOjFiMGE0OWUyLWIyNWEtNGE0Zi1iNTViLWJlNWI0ZmE0NDk2MQ==";

            var accessToken = GetAccessToken(authKey);


            var messages = new List<ChatMessage>
            {
                new("system",
                    "Ты дружелюбный помощник для начинающего C#-разработчика. " +
                    "Отвечай коротко, по делу и на русском языке."),
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
