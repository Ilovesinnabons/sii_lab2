namespace sii_lab2;

public partial class MainPage : ContentPage
{
    private const string NotSet = "—";   // "значение не задано" в начальном состоянии РБД

    private readonly ParameterBase _params;
    private readonly RuleBase _rules;
    private readonly BackwardEngine _engine;

    private bool _loaded;

    // Выпадающие списки начального состояния РБД: параметр -> Picker
    private readonly Dictionary<string, Picker> _startPickers = new();

    // Строки целевой ситуации и строки условий в редакторе правил: [параметр] = [значение] [✕]
    private readonly List<(Picker Param, Picker Value, View Row)> _goalRows = new();
    private readonly List<(Picker Param, Picker Value, View Row)> _conditionRows = new();

    // Состояние редактора правил
    private int? _editingIndex;          // null — новое правило, иначе номер изменяемого

    public MainPage()
    {
        InitializeComponent();

        string dir = FileSystem.AppDataDirectory;
        _params = new ParameterBase(Path.Combine(dir, "params.txt"));
        _rules = new RuleBase(Path.Combine(dir, "rules.txt"));
        _engine = new BackwardEngine(_rules);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;

        try
        {
            await _params.LoadAsync();
            await _rules.LoadAsync();
            _params.SyncWithRules(_rules);
            RulesFileLabel.Text = "Файл: " + _rules.FilePath;
        }
        catch (Exception ex)
        {
            ShowError("Ошибка загрузки: " + ex.Message);
        }

        BuildStartPanel();
        BuildGoalPanel(new List<Fact>());
        BuildParamsPanel();
        BuildRulesPanel();
    }

    // ================= ДОКАЗАТЕЛЬСТВО =================

    private void OnProveClicked(object sender, EventArgs e)
    {
        ClearError();

        // Начальное состояние РБД: только те параметры, у которых выбрано значение
        var startFacts = _startPickers
            .Where(kv => kv.Value.SelectedItem is string v && v != NotSet)
            .Select(kv => new Fact(kv.Key, (string)kv.Value.SelectedItem))
            .ToList();

        var goal = ReadFacts(_goalRows, "целевой ситуации");
        if (goal == null) return;
        if (goal.Count == 0)
        {
            ShowError("Задайте хотя бы один факт целевой ситуации");
            return;
        }

        bool proved = _engine.Run(startFacts, goal);

        ResultLabel.Text = proved
            ? "Цель достижима: доказано"
            : "Цель недостижима: опровергнуто";
        ResultLabel.TextColor = proved ? Colors.Green : Colors.Red;

        MemoryLabel.Text = _engine.Memory.Count == 0
            ? "(пусто)"
            : string.Join("\n", _engine.Memory.Select(kv =>
                $"{kv.Key} = {kv.Value}" + (_engine.Derived.Contains(new Fact(kv.Key, kv.Value)) ? "   (выведено)" : "")));
        LogLabel.Text = string.Join("\n", _engine.Log);
    }

    // ================= ЦЕЛЕВАЯ СИТУАЦИЯ =================

    // Перестроить строки цели (например, после изменения справочника параметров), сохранив выбранные факты
    private void BuildGoalPanel(List<Fact> facts)
    {
        GoalPanel.Children.Clear();
        _goalRows.Clear();
        foreach (var f in facts)
            AddFactRow(GoalPanel, _goalRows, f);
        if (_goalRows.Count == 0)
            AddFactRow(GoalPanel, _goalRows, null);
    }

    private void OnAddGoalFactClicked(object sender, EventArgs e) =>
        AddFactRow(GoalPanel, _goalRows, null);

    // ================= НАЧАЛЬНОЕ СОСТОЯНИЕ РБД =================

    // Для каждого параметра — выпадающий список "—" + его значения.
    // Уже выбранные значения сохраняются при перестроении (например, после добавления параметра).
    private void BuildStartPanel()
    {
        var previous = _startPickers.ToDictionary(kv => kv.Key, kv => kv.Value.SelectedItem as string);
        _startPickers.Clear();
        StartPanel.Children.Clear();

        foreach (var p in _params.Parameters)
        {
            var items = new List<string> { NotSet };
            items.AddRange(p.Values);

            var picker = new Picker { ItemsSource = items, SelectedIndex = 0 };
            if (previous.TryGetValue(p.Name, out var old) && old != null && items.Contains(old))
                picker.SelectedItem = old;

            _startPickers[p.Name] = picker;
            StartPanel.Children.Add(Row(new Label { Text = p.Name, VerticalOptions = LayoutOptions.Center }, picker));
        }
    }

    // ================= ПАРАМЕТРЫ =================

    private void BuildParamsPanel()
    {
        ParamsPanel.Children.Clear();

        foreach (var p in _params.Parameters)
        {
            string name = p.Name;
            var label = new Label { Text = p.ToString(), VerticalOptions = LayoutOptions.Center };

            var addValue = SmallButton("+ значение", async () => await AddValue(name));
            var removeValue = SmallButton("− значение", async () => await RemoveValue(name));
            var delete = SmallButton("✕", async () => await DeleteParameter(name));

            ParamsPanel.Children.Add(Row(label, addValue, removeValue, delete));
        }
    }

    private async void OnAddParameterClicked(object sender, EventArgs e)
    {
        string? name = await DisplayPromptAsync("Новый параметр", "Название параметра (например, доминантность):");
        if (string.IsNullOrWhiteSpace(name)) return;

        string? values = await DisplayPromptAsync("Значения параметра",
            "Возможные значения через запятую (например: высокая, средняя, низкая):");
        if (values == null) return;

        TryChangeParams(() => _params.AddParameter(name, values.Split(',')));
    }

    private async Task AddValue(string name)
    {
        string? value = await DisplayPromptAsync("Новое значение", $"Значение параметра «{name}»:");
        if (string.IsNullOrWhiteSpace(value)) return;

        TryChangeParams(() =>
        {
            if (!_params.AddValue(name, value))
                throw new InvalidOperationException("Такое значение уже есть");
            _params.Save();
        });
    }

    private async Task RemoveValue(string name)
    {
        var values = _params.ValuesOf(name);
        if (values.Count == 0) return;

        string? value = await DisplayActionSheet($"Удалить значение параметра «{name}»", "Отмена", null, values.ToArray());
        if (value == null || value == "Отмена") return;

        // Нельзя удалить значение, на которое ссылаются правила, иначе правила станут "битыми"
        if (_rules.Uses(name, value))
        {
            ShowError($"Значение «{value}» используется в правилах. Сначала измените или удалите эти правила.");
            return;
        }
        TryChangeParams(() => _params.RemoveValue(name, value));
    }

    private async Task DeleteParameter(string name)
    {
        if (_rules.Uses(name))
        {
            ShowError($"Параметр «{name}» используется в правилах. Сначала измените или удалите эти правила.");
            return;
        }

        string? answer = await DisplayActionSheet($"Удалить параметр «{name}»?", "Отмена", "Удалить");
        if (answer != "Удалить") return;

        TryChangeParams(() => _params.RemoveParameter(name));
    }

    // Выполнить изменение справочника параметров и перерисовать всё, что от него зависит
    private void TryChangeParams(Action change)
    {
        try
        {
            change();
            ClearError();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        BuildParamsPanel();
        BuildStartPanel();
        BuildGoalPanel(_goalRows
            .Where(r => r.Param.SelectedItem is string o && r.Value.SelectedItem is string v
                        && _params.ValuesOf(o).Contains(v))
            .Select(r => new Fact((string)r.Param.SelectedItem, (string)r.Value.SelectedItem))
            .ToList());
    }

    // ================= БАЗА ПРАВИЛ =================

    private void BuildRulesPanel()
    {
        RulesPanel.Children.Clear();

        for (int i = 0; i < _rules.Rules.Count; i++)
        {
            int index = i;   // копия для лямбд
            var label = new Label { Text = $"{i + 1}. {_rules.Rules[i]}", VerticalOptions = LayoutOptions.Center };
            var edit = SmallButton("✎", async () => await OpenRuleEditor(index));
            var delete = SmallButton("✕", () => { DeleteRule(index); return Task.CompletedTask; });

            RulesPanel.Children.Add(Row(label, edit, delete));
        }
    }

    private void DeleteRule(int index)
    {
        _rules.Delete(index);
        if (RuleEditorPanel.IsVisible && _editingIndex != null)
            CloseRuleEditor();   // номера правил сдвинулись, открытый редактор больше не актуален
        BuildRulesPanel();
    }

    private async void OnNewRuleClicked(object sender, EventArgs e) =>
        await OpenRuleEditor(null);

    // ================= РЕДАКТОР ПРАВИЛА =================

    // index: номер изменяемого правила или null для нового
    private async Task OpenRuleEditor(int? index)
    {
        ClearError();
        _editingIndex = index;

        ConditionsPanel.Children.Clear();
        _conditionRows.Clear();

        Rule? rule = index != null ? _rules.Rules[index.Value] : null;
        var conditions = rule?.Conditions ?? new List<Fact>();

        foreach (var c in conditions)
            AddFactRow(ConditionsPanel, _conditionRows, c);
        if (_conditionRows.Count == 0)
            AddFactRow(ConditionsPanel, _conditionRows, null);

        ConclusionParamPicker.ItemsSource = _params.Names();
        ConclusionParamPicker.SelectedItem = rule?.Conclusion.Obj;
        ConclusionValuePicker.ItemsSource = _params.ValuesOf(rule?.Conclusion.Obj);
        ConclusionValuePicker.SelectedItem = rule?.Conclusion.Value;

        RuleEditorTitle.Text = index == null ? "Новое правило" : $"Изменение правила {index + 1}";
        RuleEditorPanel.IsVisible = true;

        await MainScroll.ScrollToAsync(RuleEditorPanel, ScrollToPosition.Start, true);
    }

    private void OnAddConditionClicked(object sender, EventArgs e) =>
        AddFactRow(ConditionsPanel, _conditionRows, null);

    // Строка факта: [параметр] = [значение] [✕]. Используется и в целевой ситуации, и в редакторе правил.
    private void AddFactRow(VerticalStackLayout panel, List<(Picker Param, Picker Value, View Row)> rows, Fact? fact)
    {
        var paramPicker = new Picker { Title = "параметр", ItemsSource = _params.Names() };
        var valuePicker = new Picker { Title = "значение" };

        // При выборе параметра подставляем в соседний список его значения
        paramPicker.SelectedIndexChanged += (_, _) =>
            valuePicker.ItemsSource = _params.ValuesOf(paramPicker.SelectedItem as string);

        if (fact != null)
        {
            paramPicker.SelectedItem = fact.Obj;
            valuePicker.SelectedItem = fact.Value;
        }

        Grid row = null!;
        var remove = SmallButton("✕", () =>
        {
            panel.Children.Remove(row);
            rows.RemoveAll(r => r.Row == row);
            return Task.CompletedTask;
        });

        row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.Add(paramPicker, 0);
        row.Add(valuePicker, 1);
        row.Add(remove, 2);

        panel.Children.Add(row);
        rows.Add((paramPicker, valuePicker, row));
    }

    private void OnConclusionParamChanged(object sender, EventArgs e) =>
        ConclusionValuePicker.ItemsSource = _params.ValuesOf(ConclusionParamPicker.SelectedItem as string);

    private void OnSaveRuleClicked(object sender, EventArgs e)
    {
        // Собираем правило из выпадающих списков и проверяем
        var conditions = ReadFacts(_conditionRows, "условиях правила");
        if (conditions == null) return;

        if (conditions.Count == 0)
        {
            ShowError("Нужно хотя бы одно условие");
            return;
        }
        if (ConclusionParamPicker.SelectedItem is not string cObj || ConclusionValuePicker.SelectedItem is not string cValue)
        {
            ShowError("Выберите параметр и значение в заключении");
            return;
        }
        if (conditions.Any(c => c.Obj == cObj))
        {
            ShowError("Параметр заключения не может стоять в условиях");
            return;
        }

        var rule = new Rule(conditions, new Fact(cObj, cValue));
        if (_editingIndex == null)
            _rules.Add(rule);
        else
            _rules.Update(_editingIndex.Value, rule);

        CloseRuleEditor();
        BuildRulesPanel();
    }

    private void OnCancelRuleClicked(object sender, EventArgs e) => CloseRuleEditor();

    private void CloseRuleEditor()
    {
        ClearError();
        RuleEditorPanel.IsVisible = false;
        _editingIndex = null;
        _conditionRows.Clear();
        ConditionsPanel.Children.Clear();
    }

    // ================= ВСПОМОГАТЕЛЬНОЕ =================

    // Прочитать факты из строк [параметр] = [значение].
    // Возвращает null и показывает ошибку, если строка не заполнена или параметр повторяется.
    private List<Fact>? ReadFacts(List<(Picker Param, Picker Value, View Row)> rows, string where)
    {
        var facts = new List<Fact>();
        foreach (var (paramPicker, valuePicker, _) in rows)
        {
            if (paramPicker.SelectedItem is not string obj || valuePicker.SelectedItem is not string value)
            {
                ShowError($"Заполните параметр и значение в каждой строке в {where} (или удалите лишнюю строку)");
                return null;
            }
            facts.Add(new Fact(obj, value));
        }
        if (facts.Select(f => f.Obj).Distinct().Count() != facts.Count)
        {
            ShowError($"Один параметр указан в {where} дважды");
            return null;
        }
        return facts;
    }

    private void ShowError(string text)
    {
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    private void ClearError() => ErrorLabel.IsVisible = false;

    private static Button SmallButton(string text, Func<Task> onClick)
    {
        var b = new Button { Text = text, Padding = new Thickness(8, 2), VerticalOptions = LayoutOptions.Center };
        b.Clicked += async (_, _) => await onClick();
        return b;
    }

    // Строка: первый элемент растягивается, остальные по размеру содержимого
    private static Grid Row(View first, params View[] rest)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Add(first, 0);

        for (int i = 0; i < rest.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.Add(rest[i], i + 1);
        }
        return grid;
    }
}
