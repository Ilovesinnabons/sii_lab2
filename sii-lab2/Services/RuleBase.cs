namespace sii_lab2;

// База правил. Хранится отдельно от программы в текстовом файле rules.txt, одно правило на строку.
public class RuleBase
{
    public string FilePath { get; }
    public List<Rule> Rules { get; } = new();

    public RuleBase(string filePath)
    {
        FilePath = filePath;
    }

    public async Task LoadAsync()
    {
        // При первом запуске копируем стартовый rules.txt из ресурсов приложения
        // в папку данных, потому что файлы внутри пакета приложения менять нельзя.
        await Storage.CopyFromPackageIfMissing("rules.txt", FilePath);

        Rules.Clear();
        var lines = await File.ReadAllLinesAsync(FilePath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("#"))
                continue;   // пустые строки и комментарии пропускаем

            try
            {
                Rules.Add(Rule.Parse(lines[i]));
            }
            catch (FormatException ex)
            {
                throw new FormatException($"rules.txt, строка {i + 1}: {ex.Message}");
            }
        }
    }

    public void Save() =>
        File.WriteAllLines(FilePath, Rules.Select(r => r.ToString()));

    // Редактирование базы правил (пункт 5): каждое изменение сразу сохраняется в файл
    public void Add(Rule rule)
    {
        Rules.Add(rule);
        Save();
    }

    public void Update(int index, Rule rule)
    {
        Rules[index] = rule;
        Save();
    }

    public void Delete(int index)
    {
        Rules.RemoveAt(index);
        Save();
    }

    // Используется ли параметр (или его значение) хоть в одном правиле
    public bool Uses(string obj, string? value = null) =>
        Rules.Any(r => r.Uses(obj, value));
}
