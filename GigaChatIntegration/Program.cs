using GigaChatIntegration.GigaChat;

namespace GigaChatIntegration
{
    internal class Program
    {
        private const string DocsFolder = "docs";
        private const string IndexFile = "index.json";

        static void Main(string[] args)
        {
            var authKey = "MDE5ZWYwMjQtZjhkNi03ZmI5LTlkNDktYWQ4MmJiODQ5OTRhOjllMGQ2MWJiLTVmODktNDE1ZC04Y2JhLTRmNDQzM2Q5OGFkNw==";

            Console.WriteLine("Подключаюсь к GigaChat...");
            var gc = new GigaChatClient(authKey!);
            var kb = new KnowledgeBase();

            // Индекс уже посчитан? Загружаем с диска (мгновенно). Нет — строим из документов
            // и сохраняем, чтобы при следующем запуске не платить за эмбеддинги снова.
            if (File.Exists(IndexFile))
            {
                kb.Load(IndexFile);
                Console.WriteLine($"Загрузил индекс с диска: {kb.Count} кусков.");
                Console.WriteLine($"(изменил документы? удали {IndexFile} — пересоберётся заново)\n");
            }
            else
            {
                Console.WriteLine($"Индексирую документы из папки {DocsFolder}/ ...");
                kb.BuildFromFolder(DocsFolder, gc);
                kb.Save(IndexFile);
                Console.WriteLine($"Готово: {kb.Count} кусков, индекс сохранён в {IndexFile}.\n");
            }

            var assistant = new DocAssistant(gc, kb);

            Console.WriteLine("=== ИИ-помощник по документам ===");
            Console.WriteLine("Задай вопрос по документам из папки docs/ своими словами — помощник сам решит,");
            Console.WriteLine("нужно ли искать. Например:");
            Console.WriteLine("  • «на сколько мне хватит доступа к курсу?»   (поищет в документах)");
            Console.WriteLine("  • «можно ли вернуть деньги?»                 (поищет в документах)");
            Console.WriteLine("  • «привет, ты кто?»                          (ответит сам, без поиска)");
            Console.WriteLine("'выход' — закончить.\n");

            while (true)
            {
                Console.Write("Вопрос: ");
                string? question = Console.ReadLine();
                if (question == "выход")
                    break;

                // Вся магия — в Ask. Хук onSearch печатает «ищу…», когда модель решила искать.
                var (answer, sources) = assistant.Ask(question,
                    query => Console.WriteLine($"  [ищу в документах: {query}]"));

                Console.WriteLine($"\n{answer}");
                if (sources.Count > 0)
                    Console.WriteLine("Источники: " +
                        string.Join(", ", sources.Select(s => $"{s.Chunk.Source} ({s.Score:0.00})")) + "\n");
                else
                    Console.WriteLine();
            }
        }
    }
}