namespace sii_lab2;

// Параметр (объект) и список его возможных значений
public class Parameter
{
    public string Name { get; }
    public List<string> Values { get; } = new();

    public Parameter(string name)
    {
        Name = name;
    }

    // Формат строки в файле: "параметр: значение1, значение2, ..."
    public override string ToString() => $"{Name}: {string.Join(", ", Values)}";
}

// Справочник параметров. Хранится в params.txt рядом с базой правил.
// Нужен, чтобы в интерфейсе выбирать параметры и значения из списков,
// а также чтобы можно было добавить новый параметр, которого ещё нет ни в одном правиле.
public class ParameterBase
{
    public string FilePath { get; }
    public List<Parameter> Parameters { get; } = new();

    public ParameterBase(string filePath)
    {
        FilePath = filePath;
    }

    public async Task LoadAsync()
    {
        await Storage.CopyFromPackageIfMissing("params.txt", FilePath);

        Parameters.Clear();
        foreach (var line in await File.ReadAllLinesAsync(FilePath))
        {
            var parts = line.Split(':');
            if (parts.Length != 2)
                continue;   // пустые или некорректные строки пропускаем

            var p = new Parameter(Normalize(parts[0]));
            foreach (var v in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                p.Values.Add(Normalize(v));
            Parameters.Add(p);
        }
    }

    public void Save() =>
        File.WriteAllLines(FilePath, Parameters.Select(p => p.ToString()));

    public Parameter? Find(string name) =>
        Parameters.FirstOrDefault(p => p.Name == name);

    public List<string> Names() =>
        Parameters.Select(p => p.Name).ToList();

    public List<string> ValuesOf(string? name) =>
        name == null ? new() : Find(name)?.Values.ToList() ?? new();

    public void AddParameter(string name, IEnumerable<string> values)
    {
        name = Normalize(name);
        if (Find(name) != null)
            throw new InvalidOperationException($"Параметр '{name}' уже есть");

        var p = new Parameter(name);
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            var value = Normalize(v);
            if (!p.Values.Contains(value))
                p.Values.Add(value);
        }
        Parameters.Add(p);
        Save();
    }

    // Добавить значение параметру (если параметра нет, он создаётся).
    // Возвращает true, если что-то действительно добавилось.
    public bool AddValue(string name, string value)
    {
        name = Normalize(name);
        value = Normalize(value);

        var p = Find(name);
        if (p == null)
        {
            p = new Parameter(name);
            Parameters.Add(p);
        }
        if (p.Values.Contains(value))
            return false;

        p.Values.Add(value);
        return true;
    }

    public void RemoveParameter(string name)
    {
        Parameters.RemoveAll(p => p.Name == name);
        Save();
    }

    public void RemoveValue(string name, string value)
    {
        Find(name)?.Values.Remove(value);
        Save();
    }

    // Если в rules.txt встречаются параметры или значения, которых нет в params.txt,
    // добавляем их, чтобы все правила можно было открыть в редакторе.
    public void SyncWithRules(RuleBase ruleBase)
    {
        bool changed = false;
        foreach (var rule in ruleBase.Rules)
            foreach (var f in rule.Conditions.Append(rule.Conclusion))
                changed |= AddValue(f.Obj, f.Value);

        if (changed)
            Save();
    }

    // Единый вид имён: нижний регистр, пробелы заменены на "_".
    // Символы = , : запрещены, потому что они служебные в файлах правил и параметров.
    public static string Normalize(string text)
    {
        var s = text.Trim().ToLowerInvariant().Replace(' ', '_');
        if (s == "")
            throw new FormatException("Пустое название");
        if (s.IndexOfAny(new[] { '=', ',', ':' }) >= 0)
            throw new FormatException($"В названии '{s}' нельзя использовать символы = , :");
        return s;
    }
}
