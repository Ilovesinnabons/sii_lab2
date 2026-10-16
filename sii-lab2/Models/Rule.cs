using System.Text.RegularExpressions;

namespace sii_lab2;

// Факт вида "объект=значение". Используется и в условиях правил, и в рабочей базе данных.
public record Fact(string Obj, string Value)
{
    public override string ToString() => $"{Obj}={Value}";

    // Разбор строки "объект=значение". Регистр приводим к нижнему, пробелы обрезаем,
    // чтобы "Валентность = Высокая" и "валентность=высокая" считались одним и тем же.
    public static Fact Parse(string text)
    {
        var parts = text.Split('=');
        if (parts.Length != 2 || parts[0].Trim() == "" || parts[1].Trim() == "")
            throw new FormatException($"Ожидалось 'объект=значение', получено: '{text.Trim()}'");

        return new Fact(parts[0].Trim().ToLowerInvariant(), parts[1].Trim().ToLowerInvariant());
    }
}

// Правило вида "ЕСЛИ усл1 И усл2 ... ТО заключение".
public class Rule
{
    public List<Fact> Conditions { get; } = new();
    public Fact Conclusion { get; private set; } = null!;

    private Rule() { }

    // Создание правила из интерфейса (условия и заключение выбраны в выпадающих списках)
    public Rule(IEnumerable<Fact> conditions, Fact conclusion)
    {
        Conditions.AddRange(conditions);
        Conclusion = conclusion;
    }

    // Используется ли в правиле параметр (или конкретное значение параметра)
    public bool Uses(string obj, string? value = null) =>
        Conditions.Append(Conclusion).Any(f => f.Obj == obj && (value == null || f.Value == value));

    public static Rule Parse(string line)
    {
        string text = line.Trim();

        if (!text.StartsWith("ЕСЛИ ", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Правило должно начинаться с 'ЕСЛИ'");

        int thenPos = text.IndexOf(" ТО ", StringComparison.OrdinalIgnoreCase);
        if (thenPos < 0)
            throw new FormatException("В правиле нет 'ТО'");

        string ifPart = text.Substring(5, thenPos - 5);   // между "ЕСЛИ " и " ТО "
        string thenPart = text.Substring(thenPos + 4);     // после " ТО "

        var rule = new Rule { Conclusion = Fact.Parse(thenPart) };

        // Условия разделены связкой "И"
        foreach (var cond in Regex.Split(ifPart, @"\s+И\s+", RegexOptions.IgnoreCase))
            rule.Conditions.Add(Fact.Parse(cond));

        return rule;
    }

    // Обратное преобразование: в таком виде правило пишется в файл и показывается на странице
    public override string ToString() =>
        $"ЕСЛИ {string.Join(" И ", Conditions)} ТО {Conclusion}";
}
