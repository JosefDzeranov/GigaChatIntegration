using GigaChatIntegration.GigaChat;
using System.Text.Json;

namespace GigaChatIntegration
{
    internal class KnowledgeBase
    {
        private List<Chunk> chunks = new();
        public int Count => chunks.Count;

        public KnowledgeBase()
        {
        }


        // Прочитать все документы из папки, порезать на куски и сэмбеддить их.
        public void BuildFromFolder(string folder, GigaChatClient gc)
        {
            var files = Directory.GetFiles(folder, "*.md")
                .Concat(Directory.GetFiles(folder, "*.txt"))
                .OrderBy(f => f)
                .ToList();

            // Собираем все куски всех документов (помним, из какого файла каждый).
            var pending = new List<(string Source, string Text)>();
            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);
                foreach (var piece in SplitIntoChunks(text))
                    pending.Add((name, piece));
            }

            // Эмбеддим куски пачками (батч-эмбеддинг — не больше N за запрос) и складываем
            // пары {кусок, вектор}. Это и есть наш векторный индекс в памяти.
            chunks = new List<Chunk>();
            foreach (var batch in Batch(pending, 50))
            {
                float[][] vectors = gc.Embed(batch.Select(p => p.Text).ToList());
                for (int i = 0; i < batch.Count; i++)
                    chunks.Add(new Chunk(batch[i].Source, batch[i].Text, vectors[i]));
            }
        }

        // Сохранить индекс на диск (вместе с векторами) и загрузить обратно.
        public void Save(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(chunks));
        }

        public void Load(string path)
        {
            chunks = JsonSerializer.Deserialize<List<Chunk>>(File.ReadAllText(path)) ?? new();
        }

        // ПОИСК ПО СМЫСЛУ: эмбеддим вопрос, считаем косинус ко всем кускам, берём top-K.
        public List<Scored> Search(string query, GigaChatClient gc, int topK)
        {
            float[] q = gc.Embed(new List<string> { query })[0];
            return chunks
                .Select(c => new Scored(c, Cosine(q, c.Vector)))
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();
        }

        // Косинусная близость (День 3): ~1 — смысл совпадает, ~0 — не связаны.
        private static double Cosine(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9);
        }

        // Режем документ на куски по пустым строкам (абзацам). Слишком короткие куски
        // (заголовок, одна строка) приклеиваем к следующему, чтобы «#Сертификат» не жил
        // отдельным куском без текста. Учебно: настоящий продакшен режет ещё и по длине
        // и с нахлёстом, но для коротких документов абзаца достаточно.
        private static IEnumerable<string> SplitIntoChunks(string text)
        {
            var paragraphs = text
                .Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0);

            string? carry = null;
            foreach (var p in paragraphs)
            {
                string piece = carry is null ? p : carry + "\n" + p;
                if (piece.Length < 80) { carry = piece; continue; }   // короткий — копим дальше
                carry = null;
                yield return piece;
            }
            if (carry is not null) yield return carry;
        }

        // Бьём список на пачки по size — для батч-эмбеддинга (не больше N текстов за запрос).
        private static IEnumerable<List<T>> Batch<T>(List<T> items, int size)
        {
            for (int i = 0; i < items.Count; i += size)
                yield return items.GetRange(i, Math.Min(size, items.Count - i));
        }
    }

    // Кусок документа: из какого файла, текст куска, вектор смысла.
    internal record Chunk(string Source, string Text, float[] Vector);

    // Результат поиска: найденный кусок + его близость к вопросу (0..1).
    internal record Scored(Chunk Chunk, double Score);
}