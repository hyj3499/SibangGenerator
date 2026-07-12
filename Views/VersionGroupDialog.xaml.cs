using System.Windows;
using System.Windows.Controls;
using SibangGenerator.Models;
using SibangGenerator.Services;

namespace SibangGenerator.Views;

/// <summary>
/// ② 단계의 [+ 모델 추가] 대화상자.
///
/// 버전 콤보박스에는 이미 등록된 버전이 나오지 않는다.
/// 그래서 한 모델이 두 버전에 들어갈 일이 구조적으로 없다.
///
/// 콤보박스는 편집 가능하다. 펌웨어 폴더를 아직 지정하지 않았어도
/// 버전을 직접 타이핑해 모델을 등록할 수 있다.
/// (BOM · 폴더 조회는 경로를 지정한 뒤에 자동으로 다시 돈다)
/// </summary>
public partial class VersionGroupDialog : Window
{
    readonly HashSet<string> _usedVersions;
    readonly HashSet<string> _usedModels;
    readonly string? _editing;

    public VersionGroup? Result { get; private set; }

    /// <param name="available">펌웨어 폴더에서 수집한 전체 버전 (비어 있어도 됨)</param>
    /// <param name="usedVersions">이미 등록된 버전 (편집 중인 것 제외)</param>
    /// <param name="usedModels">다른 그룹에 이미 등록된 모델</param>
    /// <param name="editing">편집 모드면 기존 그룹</param>
    public VersionGroupDialog(
        IEnumerable<string> available,
        IEnumerable<string> usedVersions,
        IEnumerable<string> usedModels,
        VersionGroup? editing = null)
    {
        InitializeComponent();

        _usedVersions = new HashSet<string>(usedVersions, StringComparer.Ordinal);
        _usedModels = new HashSet<string>(usedModels, StringComparer.Ordinal);
        _editing = editing?.Version;

        // 편집 중인 버전은 선택지에 남겨둔다
        var choices = available
            .Where(v => !_usedVersions.Contains(v) || v == _editing)
            .ToList();

        CbVersion.ItemsSource = choices;

        VerHint.Text = choices.Count > 0
            ? "펌웨어 폴더명에서 수집했습니다. 목록에 없으면 직접 입력해도 됩니다."
            : "펌웨어 폴더를 아직 지정하지 않았습니다. 버전을 직접 입력하세요 (예: 1.00.7a).";

        if (editing is not null)
        {
            Head.Text = $"모델 편집 · {editing.Version}";
            Title = "모델 편집";
            CbVersion.Text = editing.Version;
            TbModels.Text = editing.RawModels;
        }
        else if (choices.Count > 0)
        {
            CbVersion.SelectedIndex = 0;
        }
    }

    /// <summary>편집 가능한 콤보박스이므로 Text 를 본다.</summary>
    string CurrentVersion => (CbVersion.Text ?? "").Trim();

    void Ver_Changed(object s, SelectionChangedEventArgs e) => Validate();
    void Ver_Typed(object s, System.Windows.Input.KeyEventArgs e) => Validate();
    void Models_Changed(object s, TextChangedEventArgs e) => Validate();

    /// <summary>입력 즉시 모델 수와 중복을 알려준다.</summary>
    void Validate()
    {
        if (!IsLoaded) return;

        string ver = CurrentVersion;
        var models = ModelResolver.ParseModels(TbModels.Text);
        var dup = models.Where(_usedModels.Contains).ToList();

        if (ver.Length == 0)
        {
            SetPreview("버전을 선택하거나 입력하세요", "Dim", false);
            return;
        }
        if (ver != _editing && _usedVersions.Contains(ver))
        {
            SetPreview($"버전 {ver} 은 이미 등록되어 있습니다", "Alert", false);
            return;
        }
        if (models.Count == 0)
        {
            SetPreview("0개 모델", "Dim", false);
            return;
        }
        if (dup.Count > 0)
        {
            SetPreview($"{models.Count}개 모델 · 다른 그룹에 이미 등록됨: {string.Join(", ", dup)}",
                "Alert", false);
            return;
        }

        SetPreview($"{models.Count}개 모델 · {string.Join(", ", models)}", "Ok", true);
    }

    void SetPreview(string text, string brushKey, bool ok)
    {
        Preview.Text = text;
        Preview.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
        BtnOk.IsEnabled = ok;
    }

    void Ok_Click(object s, RoutedEventArgs e)
    {
        string ver = CurrentVersion;
        if (ver.Length == 0) return;

        var models = ModelResolver.ParseModels(TbModels.Text);
        if (models.Count == 0) return;

        Result = new VersionGroup
        {
            Version = ver,
            RawModels = TbModels.Text,
            Models = models
        };
        DialogResult = true;
    }

    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
