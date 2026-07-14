using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SibangGenerator.Models;
using SibangGenerator.Services;

namespace SibangGenerator.Views;

/// <summary>
/// 조회 결과의 한 행을 클릭했을 때 뜨는 대화상자.
///
/// BOM 을 직접 지정하면 엑셀 조회 결과를 무시하고 그 값을 쓴다.
/// 조회에 성공했든 실패했든 상관없이 덮어쓸 수 있다.
///
/// 입력하는 즉시 {BOM}_{Ver} 폴더가 실제로 있는지 확인해 보여준다.
/// </summary>
public partial class BomOverrideDialog : Window
{
    readonly ResolvedModel _model;
    readonly FirmwareScanner _fw;

    /// <summary>적용 결과. null 이면 "직접 지정 해제".</summary>
    public string? Result { get; private set; }
    public bool Cleared { get; private set; }

    public BomOverrideDialog(ResolvedModel model, FirmwareScanner fw)
    {
        InitializeComponent();
        _model = model;
        _fw = fw;

        Head.Text = model.Name;
        Sub.Text = $"버전 {model.Version}";

        Current.Text = model.MergedCell
            ? "엑셀에서 병합된 셀에 있어 BOM 을 확정할 수 없습니다."
            : model.ExcelBom is not null
                ? $"엑셀 조회: {model.ExcelBom}"
                : "엑셀에서 BOM 을 찾지 못했습니다.";

        if (model.IsOverridden)
            Current.Text += $"\n직접 지정: {model.ManualBom} (적용 중)";

        // 후보: 해당 버전의 폴더가 실제로 있는 BOM
        var boms = fw.DiscoverBoms(model.Version);
        CbBom.ItemsSource = boms;
        CbBom.Text = model.Bom ?? "";

        BtnClear.IsEnabled = model.IsOverridden;

        Validate();
    }

    string Typed => (CbBom.Text ?? "").Trim();

    void Bom_Typed(object s, System.Windows.Input.KeyEventArgs e) => Validate();
    void Bom_Changed(object s, SelectionChangedEventArgs e) => Validate();

    /// <summary>입력한 BOM 으로 폴더가 실제 존재하는지 즉시 확인한다.</summary>
    void Validate()
    {
        if (!IsLoaded) return;

        string bom = Typed;

        if (bom.Length == 0)
        {
            FolderCheck.Text = "BOM 을 입력하세요.";
            FolderCheck.Foreground = (Brush)FindResource("Dim");
            FileList.Text = "";
            BtnOk.IsEnabled = false;
            return;
        }

        BtnOk.IsEnabled = true;
        string folder = $"{bom}_{_model.Version}";

        if (!_fw.Ready)
        {
            FolderCheck.Text = $"{folder} — 펌웨어 루트가 지정되지 않아 확인할 수 없습니다.";
            FolderCheck.Foreground = (Brush)FindResource("Amber");
            FileList.Text = "";
            return;
        }

        if (!_fw.FolderExists(bom, _model.Version))
        {
            FolderCheck.Text = $"{folder} — 폴더를 찾을 수 없습니다.";
            FolderCheck.Foreground = (Brush)FindResource("Alert");
            FileList.Text = "";
            return;
        }

        var files = _fw.ReadFiles(bom, _model.Version);
        FolderCheck.Text = $"{folder} — 파일 {files.Count}개 확인";
        FolderCheck.Foreground = (Brush)FindResource("Ok");
        FileList.Text = string.Join("\n", files);
    }

    void Ok_Click(object s, RoutedEventArgs e)
    {
        if (Typed.Length == 0) return;
        Result = Typed;
        Cleared = false;
        DialogResult = true;
    }

    /// <summary>직접 지정을 지우고 엑셀 조회 결과로 되돌린다.</summary>
    void Clear_Click(object s, RoutedEventArgs e)
    {
        Result = null;
        Cleared = true;
        DialogResult = true;
    }

    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
