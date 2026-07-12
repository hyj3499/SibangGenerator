using System.Windows;
using System.Windows.Controls;
using SibangGenerator.Models;

namespace SibangGenerator.Views;

public sealed class RuleKindVm
{
    public TransformKind Kind { get; init; }
    public string Sid { get; init; } = "";
    public string Title { get; init; } = "";
    public string Desc { get; init; } = "";
    public string Info { get; init; } = "";
}

public partial class RuleDialog : Window
{
    static readonly RuleKindVm[] Catalog =
    {
        new() { Kind = TransformKind.ModelList, Sid = "T1", Title = "모델 리스트 교체",
                Desc = "\"1. 모델이름 : A, B, C\" 의 나열을 등록 모델로 통째 교체합니다.",
                Info = "콜론 앞의 라벨은 그대로 두고 뒤만 바꿉니다. 영문 블록에도 같은 규칙을 따로 걸어주세요." },

        new() { Kind = TransformKind.ProductVersion, Sid = "T2", Title = "제품버전 재생성",
                Desc = "하위 4-N 블록을 전부 버리고 등록 모델로 새로 만듭니다.",
                Info = "같은 BOM + 같은 버전 → 한 블록에 모델 나열. 같은 BOM이라도 버전이 다르면 별개 블록.\n\n"
                     + "블록 순서는 ② 단계 입력 순서를 그대로 따릅니다.\n"
                     + "F/W 줄은 {BOM}_{Ver} 폴더를 읽어 조립합니다 — 고정 3종 먼저, 나머지 파일 전부를 파일명 오름차순으로." },

        new() { Kind = TransformKind.ListFilter, Sid = "T3", Title = "모델 리스트 필터",
                Desc = "제조환경 안의 모델 나열을 A / B / C 옵션에 따라 걸러냅니다.",
                Info = "" },

        new() { Kind = TransformKind.SwVersionLine, Sid = "T4", Title = "S/W Version 줄 재작성",
                Desc = "JIG 절차의 S/W Version 줄을 등록 버전에 맞게 다시 씁니다.",
                Info = "버전이 1개면  →  3) S/W Version : 1.00.6a 확인\n\n"
                     + "버전이 2개 이상이면 하위 항목으로 펼칩니다\n"
                     + "  3) S/W Version 확인\n"
                     + "      - 1.00.7a 확인 모델 : ...\n"
                     + "      - 1.00.8a 확인 모델 : ..." },

        new() { Kind = TransformKind.DropBlock, Sid = "T5", Title = "단락 삭제",
                Desc = "이 단락을 통째로 삭제합니다.",
                Info = "\"6-1) 구미 재작업 공정\" 이 필요 없을 때 씁니다.\n"
                     + "삭제 후 남은 \"6-2) JIG\" 는 번호 재부여 패스에서 자동으로 6-1) 이 됩니다." },
    };

    readonly Block _block;
    readonly TransformRule? _editing;
    RuleKindVm? _picked;

    public TransformRule? Result { get; private set; }

    /// <param name="editing">null 이면 새로 추가, 아니면 기존 규칙 수정</param>
    public RuleDialog(Block block, TransformRule? editing = null)
    {
        InitializeComponent();
        _block = block;
        _editing = editing;
        KindList.ItemsSource = Catalog;
        Subtitle.Text = $"{block.Num} · {block.Title}   →   앵커 \"{block.Anchor}\"";

        if (editing is null) return;

        // 수정 모드: 종류는 고정하고 설정만 바꾼다
        Title = "변환 규칙 수정";
        BtnAdd.Content = "저장";
        KindList.SelectedItem = Catalog.First(x => x.Kind == editing.Kind);
        KindList.IsEnabled = false;

        RbA.IsChecked = editing.Filter == FilterMode.KeepAll;
        RbB.IsChecked = editing.Filter == FilterMode.OnlyRegistered;
        RbC.IsChecked = editing.Filter == FilterMode.DropUnrelatedGroups;
    }

    void Kind_Select(object s, SelectionChangedEventArgs e)
    {
        _picked = KindList.SelectedItem as RuleKindVm;
        if (_picked is null) return;

        BtnAdd.IsEnabled = true;

        bool isFilter = _picked.Kind == TransformKind.ListFilter;
        FilterBox.Visibility = isFilter ? Visibility.Visible : Visibility.Collapsed;

        InfoBox.Visibility = (!isFilter && _picked.Info.Length > 0)
            ? Visibility.Visible : Visibility.Collapsed;
        InfoText.Text = _picked.Info;
    }

    void Add_Click(object s, RoutedEventArgs e)
    {
        if (_picked is null) return;

        Result = new TransformRule
        {
            Kind = _picked.Kind,
            Bid = _block.Bid,
            Num = _block.Num,
            Title = _block.Title,
            AnchorKeyword = _block.Anchor,
            AnchorNumberAtAuthoring = _block.Num,
            Filter = RbB.IsChecked == true ? FilterMode.OnlyRegistered
                   : RbC.IsChecked == true ? FilterMode.DropUnrelatedGroups
                   : FilterMode.KeepAll
        };
        DialogResult = true;
    }

    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
