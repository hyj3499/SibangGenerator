using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SibangGenerator.Models;
using SibangGenerator.Services;

namespace SibangGenerator.Views;

public sealed class LineVm
{
    public int No { get; init; }
    public string Text { get; init; } = "";
    public int Index { get; init; }
    public Brush RowBrush { get; init; } = Brushes.Transparent;
}

public sealed class LogVm
{
    public Severity Sev { get; init; }
    public string Msg { get; init; } = "";
    public string Label => Sev switch
    {
        Severity.Error => "오류",
        Severity.Warning => "경고",
        Severity.Info => "정보",
        _ => "통과"
    };
}

public sealed class StepVm
{
    public int No { get; init; }
    public string Label { get; init; } = "";
    public bool Enabled { get; init; }
    public Brush Bg { get; init; } = Brushes.Transparent;
    public Brush Fg { get; init; } = Brushes.Black;
    public Brush NumBg { get; init; } = Brushes.Transparent;
    public Brush NumFg { get; init; } = Brushes.Black;
}

public partial class MainWindow : Window
{
    readonly SpecParser _parser = new();
    readonly ObservableCollection<LineVm> _l3 = new(), _lOld = new(), _lNew = new();
    readonly ObservableCollection<VersionGroup> _groups = new();
    readonly ObservableCollection<ResolvedModel> _resolved = new();
    readonly ObservableCollection<Block> _blocks = new();      // ④ 규칙 대상 (영문 숨김)
    readonly ObservableCollection<Block> _summary = new();     // ③ 구조 요약
    readonly ObservableCollection<TransformRule> _rules = new();
    readonly ObservableCollection<LogVm> _log = new();

    int _step = 1, _maxStep = 1, _seq = 1;
    GenerationResult? _last;
    List<string> _availableVersions = new();

    /// <summary>%APPDATA% 에 저장되는 전역 설정. 프로그램을 껐다 켜도 유지된다.</summary>
    readonly AppSettings _app = AppSettings.Load();

    static readonly SolidColorBrush Ink = new(Color.FromRgb(0x12, 0x16, 0x1C));
    static readonly SolidColorBrush Paper = new(Color.FromRgb(0xE8, 0xE6, 0xE0));
    static readonly SolidColorBrush SelBg = new(Color.FromArgb(0x1A, 0x1B, 0x4F, 0x8F));
    static readonly SolidColorBrush EnBg = new(Color.FromArgb(0x0C, 0x00, 0x00, 0x00));

    public MainWindow()
    {
        InitializeComponent();
        Lv3.ItemsSource = _l3;
        LvOld.ItemsSource = _lOld;
        LvNew.ItemsSource = _lNew;
        GroupList.ItemsSource = _groups;
        ResolveList.ItemsSource = _resolved;
        BlockList.ItemsSource = _blocks;
        BlockSummary.ItemsSource = _summary;
        RuleList.ItemsSource = _rules;
        LogBox.ItemsSource = _log;

        RestoreAppSettings();
        DrawSteps();
    }

    /// <summary>저장해둔 경로 · 사전을 UI 에 되살린다.</summary>
    void RestoreAppSettings()
    {
        ApplySettings(_app.ToWorkspace());

        if (_app.ModelDictionary.Count > 0)
        {
            _parser.SetDictionary(_app.ModelDictionary);
            TbDict.Text = string.Join(", ", _app.ModelDictionary);
        }

        if (!string.IsNullOrWhiteSpace(_app.FirmwareRoot) ||
            !string.IsNullOrWhiteSpace(_app.ExcelPath))
            Say("저장된 경로를 불러왔습니다.");
    }

    /// <summary>창을 닫을 때 현재 경로 · 사전을 저장한다.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _app.CopyFrom(ReadSettings(), _parser.Dictionary);
        _app.Save();
        base.OnClosing(e);
    }

    // ═══ 스텝바 ═══════════════════════════════════

    static readonly (int No, string Label)[] StepDefs =
    {
        (1, "기존 시방"), (2, "모델 · 경로"), (3, "구조 분석"), (4, "변환 규칙"), (5, "미리보기")
    };

    void DrawSteps() => Stepper.ItemsSource = StepDefs.Select(s =>
    {
        bool on = _step == s.No, done = _maxStep > s.No;
        return new StepVm
        {
            No = s.No,
            Label = s.Label,
            Enabled = _maxStep >= s.No,
            Bg = on ? Ink : Brushes.Transparent,
            Fg = on ? Paper : Ink,
            NumBg = done && !on ? Ink : Brushes.Transparent,
            NumFg = done && !on ? Paper : (on ? Paper : Ink)
        };
    }).ToList();

    void Go(int n)
    {
        if (n > _maxStep) return;
        _step = n;
        P1.Visibility = n == 1 ? Visibility.Visible : Visibility.Collapsed;
        P2.Visibility = n == 2 ? Visibility.Visible : Visibility.Collapsed;
        P3.Visibility = n == 3 ? Visibility.Visible : Visibility.Collapsed;
        P4.Visibility = n == 4 ? Visibility.Visible : Visibility.Collapsed;
        P5.Visibility = n == 5 ? Visibility.Visible : Visibility.Collapsed;
        DrawSteps();

        if (n == 2) Render2();
        if (n == 3) Render3();
        if (n == 4) Render4();
    }

    void Unlock(int n) { if (n > _maxStep) { _maxStep = n; DrawSteps(); } }
    void Step_Click(object s, RoutedEventArgs e) { if (s is Button b && b.Tag is int n) Go(n); }
    void Say(string m) => Status.Text = m;

    // ═══ 1. 기존 시방 첨부 ═══════════════════════════

    void Pick_File(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "텍스트 파일|*.txt;*.md|모든 파일|*.*" };
        if (d.ShowDialog() == true) LoadDoc(d.FileName);
    }

    void Drag_Over(object s, DragEventArgs e) =>
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

    void Drop_File(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) LoadDoc(f[0]);
    }

    void LoadDoc(string path)
    {
        try
        {
            _parser.Load(SpecParser.ReadTextAuto(path));

            // 문서에서 자동 수집한 모델명 + 저장해둔 사전을 합친다.
            // 저장해둔 것이 우선 순서를 갖는다.
            var merged = new List<string>(_app.ModelDictionary);
            foreach (var m in _parser.Dictionary)
                if (!merged.Contains(m)) merged.Add(m);

            _parser.SetDictionary(merged);
            TbDict.Text = string.Join(", ", merged);

            _groups.Clear(); _rules.Clear(); _resolved.Clear();
            _maxStep = 2;
            Unlock(2);
            Go(2);
            Say($"{Path.GetFileName(path)} · {_parser.Lines.Length}줄 · 사전 {merged.Count}개");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽을 수 없습니다.\n\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══ 2. 모델 · 경로 ════════════════════════════

    WorkspaceSettings ReadSettings() => new()
    {
        FirmwareRoot = TbRoot.Text.Trim(),
        ExcelPath = TbExcel.Text.Trim(),
        SheetName = TbSheet.Text.Trim(),
        MatchColumn = TbMcol.Text.Trim(),
        ValueColumn = TbVcol.Text.Trim(),
        SplitCell = ChkSplit.IsChecked == true,
        Cpu = TbCpu.Text.Trim(),
        FixedFilePrefixes = TbFixed.Text.Trim()
    };

    void ApplySettings(WorkspaceSettings w)
    {
        if (!string.IsNullOrWhiteSpace(w.FirmwareRoot)) TbRoot.Text = w.FirmwareRoot;
        if (!string.IsNullOrWhiteSpace(w.ExcelPath)) TbExcel.Text = w.ExcelPath;
        if (!string.IsNullOrWhiteSpace(w.SheetName)) TbSheet.Text = w.SheetName;
        if (!string.IsNullOrWhiteSpace(w.MatchColumn)) TbMcol.Text = w.MatchColumn;
        if (!string.IsNullOrWhiteSpace(w.ValueColumn)) TbVcol.Text = w.ValueColumn;
        if (!string.IsNullOrWhiteSpace(w.Cpu)) TbCpu.Text = w.Cpu;
        if (!string.IsNullOrWhiteSpace(w.FixedFilePrefixes)) TbFixed.Text = w.FixedFilePrefixes;
        ChkSplit.IsChecked = w.SplitCell;
    }

    void Render2()
    {
        RefreshVersions();
        RefreshGroups();
    }

    /// <summary>펌웨어 폴더명에서 버전 목록을 다시 수집한다.</summary>
    void RefreshVersions()
    {
        var fw = new FirmwareScanner(ReadSettings());
        if (!fw.Ready)
        {
            _availableVersions = new();
            RootInfo.Text = "경로를 찾을 수 없습니다";
            RootInfo.Foreground = (Brush)FindResource("Alert");
        }
        else
        {
            _availableVersions = fw.DiscoverVersions();
            RootInfo.Text = $"버전 {_availableVersions.Count}종 발견 · {string.Join(", ", _availableVersions)}";
            RootInfo.Foreground = (Brush)FindResource("Ok");
        }
        Gate2();
    }

    void Path_Changed(object s, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshVersions();
        if (_groups.Count > 0) RefreshGroups();   // 경로가 바뀌면 조회 결과도 갱신
    }

    void PickRoot_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "펌웨어 루트 폴더 선택" };
        if (d.ShowDialog() == true) TbRoot.Text = d.FolderName;
    }

    void PickExcel_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Excel|*.xlsx;*.xlsm|모든 파일|*.*" };
        if (d.ShowDialog() == true) TbExcel.Text = d.FileName;
    }

    void Dict_Changed(object s, TextChangedEventArgs e)
    {
        _parser.SetDictionary(TbDict.Text.Split(new[] { ',', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries));
        DictN.Text = $"{_parser.Dictionary.Count}개 등록";
    }

    void En_Toggle(object s, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _parser.UseEnglish = ChkEn.IsChecked == true;
        if (!_parser.UseEnglish) _parser.EnglishAt = -1;
        if (_step == 3 && _parser.Lines.Length > 0) Render3();
    }

    // ── 버전 그룹 ──────────────────────────────────

    void AddGroup_Click(object s, RoutedEventArgs e)
    {
        // 펌웨어 폴더나 엑셀이 없어도 모델을 등록할 수 있다.
        // 버전은 직접 타이핑하면 되고, BOM · 폴더 조회는 경로를 넣으면 자동으로 다시 돈다.
        var dlg = new VersionGroupDialog(
            _availableVersions,
            _groups.Select(g => g.Version),
            _groups.SelectMany(g => g.Models)) { Owner = this };

        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        _groups.Add(dlg.Result);
        RefreshGroups();
        Say($"[{dlg.Result.Version}] {dlg.Result.Models.Count}개 모델 등록");
    }

    void EditGroup_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button b || b.Tag is not string ver) return;
        var g = _groups.FirstOrDefault(x => x.Version == ver);
        if (g is null) return;

        var dlg = new VersionGroupDialog(
            _availableVersions,
            _groups.Where(x => x != g).Select(x => x.Version),
            _groups.Where(x => x != g).SelectMany(x => x.Models),
            g) { Owner = this };

        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        int i = _groups.IndexOf(g);
        _groups[i] = dlg.Result;
        RefreshGroups();
    }

    void DelGroup_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button b || b.Tag is not string ver) return;
        var g = _groups.FirstOrDefault(x => x.Version == ver);
        if (g is not null) _groups.Remove(g);
        RefreshGroups();
    }

    /// <summary>
    /// 그룹이 바뀔 때마다 BOM · 폴더를 다시 조회해 보여준다.
    ///
    /// 펌웨어 루트나 엑셀이 없어도 조회 결과를 표시한다.
    /// 없는 항목은 "조회 실패"로 나오고, 사용자가 행을 클릭해 BOM 을 직접 지정할 수 있다.
    /// </summary>
    void RefreshGroups()
    {
        GroupEmpty.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int total = _groups.Sum(g => g.Models.Count);
        GroupInfo.Text = $"{_groups.Count}개 버전 · 모델 {total}개";

        _resolved.Clear();

        if (_groups.Count == 0)
        {
            ResolveBox.Visibility = Visibility.Collapsed;
            Gate2();
            return;
        }

        var ws = ReadSettings();

        // 둘 중 하나가 없어도 진행한다. Resolve 가 null 을 허용한다.
        using var bom = new BomLookup(ws);
        var fw = new FirmwareScanner(ws);

        foreach (var m in ModelResolver.Resolve(_groups, bom, fw)) _resolved.Add(m);

        ResolveBox.Visibility = Visibility.Visible;
        Gate2();
    }

    /// <summary>[↻ 갱신] — 경로를 바꾼 뒤 다시 조회한다.</summary>
    void RefreshResolve_Click(object s, RoutedEventArgs e)
    {
        RefreshVersions();
        RefreshGroups();

        int ok = _resolved.Count(m => m.StatusSev == Severity.Pass);
        Say($"조회 갱신 · {ok}/{_resolved.Count}개 확인됨");
    }

    /// <summary>조회 결과 행을 더블클릭하면 BOM 을 직접 지정한다.</summary>
    void Resolve_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResolveList.SelectedItem is not ResolvedModel m) return;

        var fw = new FirmwareScanner(ReadSettings());
        var dlg = new BomOverrideDialog(m, fw) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // 오버라이드는 VersionGroup 에 저장해야 갱신해도 살아남는다
        var g = _groups.FirstOrDefault(x => x.Models.Contains(m.Name));
        if (g is null) return;

        if (dlg.Cleared)
        {
            g.BomOverrides.Remove(m.Name);
            Say($"{m.Name} · 직접 지정 해제");
        }
        else if (dlg.Result is not null)
        {
            g.BomOverrides[m.Name] = dlg.Result;
            Say($"{m.Name} · BOM 을 {dlg.Result} 로 지정");
        }

        RefreshGroups();

        // 방금 편집한 행을 다시 선택
        var again = _resolved.FirstOrDefault(x => x.Name == m.Name);
        if (again is not null) ResolveList.SelectedItem = again;
    }

    void Gate2()
    {
        bool noGroups = _groups.Count == 0;
        int bad = _resolved.Count(m => m.StatusSev == Severity.Error);

        BtnNext2.IsEnabled = !noGroups;
        Next2Hint.Text = noGroups ? "모델을 하나 이상 등록하세요"
                       : bad > 0 ? $"{bad}개 모델의 BOM·폴더 확인 실패 (행을 더블클릭해 직접 지정)"
                       : "모든 모델의 BOM · 폴더 확인됨";
    }

    void Next2_Click(object s, RoutedEventArgs e) { Unlock(3); Go(3); }

    // ═══ 3. 구조 분석 ══════════════════════════════

    void Render3()
    {
        _parser.Analyze();

        _l3.Clear();
        for (int i = 0; i < _parser.Lines.Length; i++)
        {
            Brush bg = Brushes.Transparent;
            if (_parser.UseEnglish && _parser.EnglishAt >= 0)
            {
                if (i == _parser.EnglishAt) bg = SelBg;
                else if (i > _parser.EnglishAt) bg = EnBg;
            }
            _l3.Add(new LineVm { No = i + 1, Text = _parser.Lines[i], Index = i, RowBrush = bg });
        }

        // ③ 요약에는 한글 단락만 (영문은 짝으로 붙는다)
        _summary.Clear();
        int koEnd = (_parser.UseEnglish && _parser.EnglishAt >= 0)
            ? _parser.EnglishAt : _parser.Lines.Length;
        foreach (var b in _parser.Blocks.Where(x => x.From < koEnd)) _summary.Add(b);

        EnBox.Visibility = _parser.UseEnglish ? Visibility.Visible : Visibility.Collapsed;
        P3Title.Text = _parser.UseEnglish ? "원문 · 영문 시작 라인 클릭" : "원문 · 구조 분석";
        EnInfo.Text = !_parser.UseEnglish ? "영문 사용 안 함"
                    : _parser.EnglishAt < 0 ? "경계를 선택하세요"
                    : $"{_parser.EnglishAt + 1}행부터 영문";
        EnLine.Text = _parser.EnglishAt < 0 ? "아직 선택 안 함"
            : $"L{_parser.EnglishAt + 1} · {_parser.Lines[_parser.EnglishAt].Trim()}";

        Next3Hint.Text = _parser.Pairs.Count > 0
            ? $"단락 {_summary.Count}개 · 한↔영 {_parser.Pairs.Count}쌍 짝지음"
            : $"단락 {_summary.Count}개 추출 · 앵커는 제목 키워드";
        Unlock(4);
    }

    void Lv3_Select(object s, SelectionChangedEventArgs e)
    {
        if (!_parser.UseEnglish || Lv3.SelectedItem is not LineVm v) return;
        _parser.EnglishAt = v.Index;
        Render3();
    }

    void Next3_Click(object s, RoutedEventArgs e) { Unlock(4); Go(4); }

    // ═══ 4. 변환 규칙 ══════════════════════════════

    void Render4()
    {
        _blocks.Clear();

        // 영문 체크박스가 켜져 있으면 영문 단락은 목록에서 숨긴다.
        // 한글 단락에 건 규칙이 짝지어진 영문 단락에 자동 적용되므로
        // 따로 규칙을 걸 필요가 없다.
        bool hideEnglish = _parser.UseEnglish && _parser.EnglishAt >= 0;

        foreach (var b in _parser.Blocks)
        {
            if (hideEnglish && b.From >= _parser.EnglishAt) continue;
            _blocks.Add(b);
        }

        BlkInfo.Text = $"{_blocks.Count}개 단락";
        EnNote.Text = hideEnglish
            ? $"영문 단락 {_parser.Pairs.Count}개는 한글 규칙이 자동 적용되므로 숨겼습니다."
            : "";

        RefreshRules();
        UpdateRuleButtons();
    }

    void Block_Select(object s, SelectionChangedEventArgs e)
    {
        BtnAddRule.IsEnabled = BlockList.SelectedItem is Block;

        // 단락 → 규칙 하이라이트 (양방향)
        if (BlockList.SelectedItem is not Block b) return;
        var r = _rules.FirstOrDefault(x => x.Bid == b.Bid);
        if (r is not null && !ReferenceEquals(RuleList.SelectedItem, r))
        {
            RuleList.SelectedItem = r;
            RuleList.ScrollIntoView(r);
        }
    }

    /// <summary>더블클릭도 여전히 규칙 추가로 동작한다 (단축).</summary>
    void Block_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e) =>
        AddRule_Click(s, e);

    void AddRule_Click(object s, RoutedEventArgs e)
    {
        if (BlockList.SelectedItem is not Block b)
        {
            MessageBox.Show("먼저 왼쪽에서 단락을 선택하세요.", "확인",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new RuleDialog(b) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        dlg.Result.No = _seq++;
        _rules.Add(dlg.Result);
        RefreshRules();
        RuleList.SelectedItem = dlg.Result;
        Say($"{dlg.Result.ShortId} 추가 · {b.Title}");
    }

    /// <summary>규칙 카드를 더블클릭하면 설정을 수정한다.</summary>
    void Rule_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e) =>
        EditRule_Click(s, e);

    void EditRule_Click(object s, RoutedEventArgs e)
    {
        if (RuleList.SelectedItem is not TransformRule r) return;

        var b = _parser.Blocks.FirstOrDefault(x => x.Bid == r.Bid);
        if (b is null) return;

        var dlg = new RuleDialog(b, r) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        int i = _rules.IndexOf(r);
        dlg.Result.No = r.No;
        _rules[i] = dlg.Result;
        RuleList.SelectedItem = dlg.Result;
        Say($"{dlg.Result.ShortId} 수정 · {dlg.Result.Detail}");
    }

    void Rule_Select(object s, SelectionChangedEventArgs e)
    {
        UpdateRuleButtons();

        if (RuleList.SelectedItem is not TransformRule r) return;

        // 규칙 → 단락 하이라이트 (양방향)
        var b = _blocks.FirstOrDefault(x => x.Bid == r.Bid);
        if (b is not null && !ReferenceEquals(BlockList.SelectedItem, b))
        {
            BlockList.SelectedItem = b;
            BlockList.ScrollIntoView(b);
        }
    }

    void UpdateRuleButtons()
    {
        int i = RuleList.SelectedIndex;
        bool has = i >= 0;
        BtnUp.IsEnabled = has && i > 0;
        BtnDown.IsEnabled = has && i < _rules.Count - 1;

        // 설정이 있는 규칙만 수정 가능
        BtnEditRule.IsEnabled = has && RuleList.SelectedItem is TransformRule r
            && r.Kind == TransformKind.ListFilter;
    }

    void MoveUp_Click(object s, RoutedEventArgs e) => Move(-1);
    void MoveDown_Click(object s, RoutedEventArgs e) => Move(+1);

    /// <summary>규칙 순서를 바꾼다. 실행 순서에 영향을 준다.</summary>
    void Move(int delta)
    {
        int i = RuleList.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _rules.Count) return;

        _rules.Move(i, j);
        RuleList.SelectedIndex = j;
        UpdateRuleButtons();
    }

    void DelRule_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not int no) return;
        var r = _rules.FirstOrDefault(x => x.No == no);
        if (r is not null) _rules.Remove(r);
        RefreshRules();
        UpdateRuleButtons();
    }

    void RefreshRules()
    {
        RuleN.Text = $"{_rules.Count}개";
        RuleEmpty.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnSave.IsEnabled = BtnRun.IsEnabled = _rules.Count > 0 && _groups.Count > 0;
    }

    // ═══ 5. 생성 · 미리보기 ════════════════════════

    void Run_Click(object s, RoutedEventArgs e)
    {
        var gen = new SpecGenerator(_parser, ReadSettings());
        _last = gen.Generate(_groups.ToList(), _rules.ToList());

        RenderPreview();
        Unlock(5);
        Go(5);
    }

    void RenderPreview()
    {
        if (_last is null) return;

        SumE.Text = _last.ErrorCount.ToString();
        SumW.Text = _last.WarnCount.ToString();
        SumL.Text = _last.Lines.Length.ToString();

        var ws = ReadSettings();
        using var bom = new BomLookup(ws);
        var fw = new FirmwareScanner(ws);
        int blocks = ModelResolver.BuildBlocks(ModelResolver.Resolve(_groups, bom, fw), ws).Count;
        SumB.Text = blocks.ToString();

        _lOld.Clear();
        for (int i = 0; i < _parser.Lines.Length; i++)
            _lOld.Add(new LineVm { No = i + 1, Text = _parser.Lines[i], Index = i });

        _lNew.Clear();
        for (int i = 0; i < _last.Lines.Length; i++)
            _lNew.Add(new LineVm { No = i + 1, Text = _last.Lines[i], Index = i });

        _log.Clear();
        foreach (var x in _last.Log.OrderBy(x => x.Sev))
            _log.Add(new LogVm { Sev = x.Sev, Msg = x.Msg });

        Say(_last.ErrorCount > 0
            ? $"생성 완료 · 오류 {_last.ErrorCount}건 — 로그를 확인하세요"
            : $"생성 완료 · {_last.Lines.Length}줄");
    }

    void SaveSpec_Click(object s, RoutedEventArgs e)
    {
        if (_last is null) return;
        var d = new SaveFileDialog { Filter = "텍스트|*.txt", FileName = "sibang_new.txt" };
        if (d.ShowDialog() != true) return;
        SpecWriter.Write(d.FileName, _last.Lines);
        Say($"{Path.GetFileName(d.FileName)} 저장");
    }

    void SaveLog_Click(object s, RoutedEventArgs e)
    {
        if (_last is null) return;
        var d = new SaveFileDialog { Filter = "텍스트|*.txt", FileName = "generation-log.txt" };
        if (d.ShowDialog() != true) return;
        File.WriteAllText(d.FileName, SpecWriter.BuildLog(_last, _groups.ToList()),
            new System.Text.UTF8Encoding(true));
        Say($"{Path.GetFileName(d.FileName)} 저장");
    }

    // ═══ 저장 / 불러오기 ═══════════════════════════

    void Save_Click(object s, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "generator-set.json" };
        if (d.ShowDialog() != true) return;

        GeneratorStore.Save(d.FileName, new GeneratorSet
        {
            UseEnglish = _parser.UseEnglish,
            BoundaryHint = _parser.EnglishAt >= 0 ? _parser.Lines[_parser.EnglishAt].Trim() : null,
            ModelDictionary = _parser.Dictionary,
            Settings = ReadSettings(),
            VersionGroups = _groups.ToList(),
            Rules = _rules.ToList()
        });
        Say($"{Path.GetFileName(d.FileName)} 저장");
    }

    void Load_Click(object s, RoutedEventArgs e)
    {
        if (_parser.Lines.Length == 0) { MessageBox.Show("먼저 기존 시방 TXT를 첨부하세요."); return; }

        var d = new OpenFileDialog { Filter = "JSON|*.json" };
        if (d.ShowDialog() != true) return;

        try
        {
            var set = GeneratorStore.Load(d.FileName);

            ApplySettings(set.Settings);
            if (set.ModelDictionary.Count > 0)
            {
                _parser.SetDictionary(set.ModelDictionary);
                TbDict.Text = string.Join(", ", _parser.Dictionary);
            }
            _parser.UseEnglish = set.UseEnglish;
            ChkEn.IsChecked = set.UseEnglish;

            _groups.Clear();
            foreach (var g in set.VersionGroups) _groups.Add(g);

            _parser.Analyze();
            var (linked, missing) = GeneratorStore.Reconnect(set, _parser);

            _rules.Clear();
            foreach (var r in linked) _rules.Add(r);
            _seq = linked.Count + 1;

            RefreshVersions();
            RefreshGroups();
            Unlock(4);
            Go(4);
            RefreshRules();

            Say($"앵커 재연결 {linked.Count}건" + (missing > 0 ? $" · 미발견 {missing}건 건너뜀" : ""));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"규칙 세트를 읽을 수 없습니다.\n\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
