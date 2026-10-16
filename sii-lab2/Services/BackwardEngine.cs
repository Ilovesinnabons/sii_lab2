namespace sii_lab2;

// Машина вывода: обратная цепочка рассуждений (от цели к фактам).
// Цель — набор фактов, соединённых И. Каждый факт цели доказывается рекурсивно:
// ищем правила, которые его выводят, и доказываем их условия как подцели.
public class BackwardEngine
{
    private readonly RuleBase _ruleBase;

    // Рабочая база данных: параметр -> значение
    public Dictionary<string, string> Memory { get; } = new();

    // Журнал доказательства; отступ строки показывает глубину подцели
    public List<string> Log { get; } = new();

    // Факты, выведенные правилами во время доказательства (по порядку)
    public List<Fact> Derived { get; } = new();

    // Подцели, которые доказываются прямо сейчас. Нужны для защиты от циклов:
    // при правилах вида A -> B и B -> A рекурсия иначе стала бы бесконечной.
    private readonly HashSet<Fact> _inProgress = new();

    public BackwardEngine(RuleBase ruleBase)
    {
        _ruleBase = ruleBase;
    }

    // Задать начальное состояние РБД и целевую ситуацию, выполнить доказательство.
    // Возвращает true, если цель достижима (все её факты доказаны).
    public bool Run(List<Fact> startFacts, List<Fact> goal)
    {
        Memory.Clear();
        Log.Clear();
        Derived.Clear();
        _inProgress.Clear();

        foreach (var f in startFacts)
            Memory[f.Obj] = f.Value;

        Log.Add("Начальное состояние РБД: " + (startFacts.Count == 0 ? "(пусто)" : string.Join(", ", startFacts)));
        Log.Add("Целевая ситуация: " + string.Join(" И ", goal));

        // Факты цели соединены И: достаточно одного недоказанного, чтобы цель была недостижима
        foreach (var g in goal)
        {
            if (!Prove(g, 0))
            {
                Log.Add($"ИТОГ: цель недостижима, не доказано {g}");
                return false;
            }
        }

        Log.Add("ИТОГ: цель достижима");
        return true;
    }

    // Доказать один факт. depth — глубина рекурсии, нужна только для отступов в журнале.
    private bool Prove(Fact goal, int depth)
    {
        string pad = new string(' ', depth * 4);

        // 1. Значение параметра уже известно: факт либо совпадает с ним, либо опровергнут
        if (Memory.TryGetValue(goal.Obj, out var known))
        {
            if (known == goal.Value)
            {
                Log.Add($"{pad}{goal}: есть в РБД, доказано");
                return true;
            }
            Log.Add($"{pad}{goal}: опровергнуто, в РБД {goal.Obj}={known}");
            return false;
        }

        // 2. Эта подцель уже доказывается выше по стеку, значит, правила зациклились
        if (_inProgress.Contains(goal))
        {
            Log.Add($"{pad}{goal}: цикл в правилах, подцель уже доказывается");
            return false;
        }

        // 3. Ищем правила, в заключении которых стоит этот факт
        var rules = new List<int>();
        for (int i = 0; i < _ruleBase.Rules.Count; i++)
            if (_ruleBase.Rules[i].Conclusion == goal)
                rules.Add(i);

        if (rules.Count == 0)
        {
            Log.Add($"{pad}{goal}: нет в РБД и нет правил, из которых его можно вывести, не доказано");
            return false;
        }

        _inProgress.Add(goal);
        try
        {
            // 4. Перебираем правила по порядку; первое правило, все условия которого доказаны, срабатывает
            foreach (int i in rules)
            {
                var rule = _ruleBase.Rules[i];
                Log.Add($"{pad}{goal}: пробуем правило {i + 1}: {rule}");

                Fact? failed = null;
                foreach (var c in rule.Conditions)
                {
                    if (!Prove(c, depth + 1))
                    {
                        failed = c;   // остальные условия этого правила проверять незачем
                        break;
                    }
                }

                if (failed == null)
                {
                    Memory[goal.Obj] = goal.Value;
                    Derived.Add(goal);
                    Log.Add($"{pad}Правило {i + 1} сработало, в РБД добавлено {goal}");
                    return true;
                }

                Log.Add($"{pad}Правило {i + 1} отброшено: не доказано {failed}");
            }
        }
        finally
        {
            _inProgress.Remove(goal);
        }

        Log.Add($"{pad}{goal}: ни одно правило не подошло, не доказано");
        return false;
    }
}
