using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SibangGenerator.Models;

// ═══════════════════════════════════════════════════════════
//  검증툴에서 그대로 가져온 타입 (SpecParser 가 의존)
// ═══════════════════════════════════════════════════════════

public enum Severity { Error, Warning, Info, Pass }

public sealed class Issue
{
    public Severity Sev { get; init; }
    public int Line { get; init; }
    public string Msg { get; init; } = "";
}

public sealed class NumberRun
{
    public string Zone { get; init; } = "KO";
    public int Indent { get; init; }
    public string Prefix { get; init; } = "";
    public List<(int Val, int Line)> Items { get; } = new();
    public string Label => string.IsNullOrEmpty(Prefix) ? "목록" : $"{Prefix}n";
}

public sealed record PreviewLine(int No, string Text);

public sealed class Block
{
    public string Bid { get; init; } = "";
    public string Num { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>제목 키워드. 번호가 바뀌어도 이걸로 다시 찾는다.</summary>
    public string Anchor { get; init; } = "";
    public int From { get; init; }
    public int To { get; init; }
    public int Indent { get; init; }
    public List<string> Models { get; init; } = new();
    public List<string> Children { get; init; } = new();
    public List<PreviewLine> Preview { get; init; } = new();

    /// <summary>
    /// "0. 시방목적", "4. 제품버전" 같은 최상위 섹션인가?
    /// 번호에 하이픈이 없고 들여쓰기가 0이면 섹션이다.
    /// UI 에서 제목바를 연노랑으로 칠하는 데 쓴다.
    /// </summary>
    public bool IsSection => Indent == 0 && !Num.Contains('-');

    public string Display => $"{Num} {Title}";
    public int ModelCount => Models.Count;
    public override string ToString() => Display;
}

// ═══════════════════════════════════════════════════════════
//  생성툴 고유 타입
// ═══════════════════════════════════════════════════════════

/// <summary>
/// ② 단계에서 사용자가 등록하는 단위.
/// 버전 하나에 모델 여러 개가 붙는다. 같은 버전을 두 번 등록할 수 없으므로
/// 한 모델은 한 버전만 갖는다.
/// </summary>
public sealed class VersionGroup
{
    public string Version { get; set; } = "";
    /// <summary>사용자가 복붙한 원본 텍스트. 편집 시 그대로 보여준다.</summary>
    public string RawModels { get; set; } = "";

    /// <summary>파싱된 모델명. 등록 순서를 유지한다.</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>
    /// 모델명 → 사용자가 직접 지정한 BOM.
    /// 조회 결과를 새로고침해도 이 값은 유지된다.
    /// </summary>
    public Dictionary<string, string> BomOverrides { get; set; } = new();

    [JsonIgnore] public string Summary => $"{Models.Count}개 모델";
}

/// <summary>
/// BOM 조회 결과. 단순 실패와 "병합 셀이라 판단 불가"를 구분한다.
///
/// 병합된 셀에 모델이 있으면 어느 행의 BOM 을 써야 할지 알 수 없다.
/// (G47, G48 이 병합돼 있으면 47행 값인지 48행 값인지 모른다)
/// 그래서 아무 값이나 가져오지 않고 사용자가 직접 지정하게 한다.
/// </summary>
public readonly struct BomFindResult
{
    public string? Bom { get; init; }
    public bool Merged { get; init; }

    public static BomFindResult Found(string bom) => new() { Bom = bom };
    public static BomFindResult NotFound => new();
    public static BomFindResult MergedCell => new() { Merged = true };

    public bool HasBom => !string.IsNullOrEmpty(Bom);
}

/// <summary>
/// 버전 그룹을 풀어서 모델 하나하나로 만든 것.
/// BOM 은 엑셀에서, 폴더 존재 여부는 디스크에서 채운다.
///
/// 사용자가 BOM 을 직접 지정하면(ManualBom) 엑셀 조회 결과보다 우선한다.
/// 조회에 실패했든 성공했든 상관없이 덮어쓴다.
/// </summary>
public sealed class ResolvedModel : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";

    /// <summary>② 단계 입력 순서. 4-N 블록 정렬에 쓴다.</summary>
    public int Order { get; init; }

    /// <summary>엑셀에서 조회한 값. 사용자가 덮어쓰지 않았을 때만 쓰인다.</summary>
    public string? ExcelBom { get; set; }

    /// <summary>
    /// 모델이 병합된 셀에서 발견되어 BOM 을 확정할 수 없는 상태.
    /// 이 경우 조회를 포기하고 사용자가 직접 지정하게 한다.
    /// </summary>
    bool _mergedCell;
    public bool MergedCell
    {
        get => _mergedCell;
        set { _mergedCell = value; NotifyAll(); }
    }

    string? _manualBom;
    /// <summary>사용자가 직접 지정한 BOM. null 이 아니면 엑셀 값을 무시한다.</summary>
    public string? ManualBom
    {
        get => _manualBom;
        set { _manualBom = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); NotifyAll(); }
    }

    /// <summary>실제로 사용될 BOM.</summary>
    public string? Bom => ManualBom ?? ExcelBom;

    public bool IsOverridden => ManualBom is not null;

    public string? FolderName => Bom is null ? null : $"{Bom}_{Version}";

    public bool BomFound => !string.IsNullOrEmpty(Bom);

    bool _folderFound;
    public bool FolderFound
    {
        get => _folderFound;
        set { _folderFound = value; NotifyAll(); }
    }

    /// <summary>폴더에서 읽은 파일명. 고정 3종 + 나머지(파일명 오름차순).</summary>
    public List<string> Files { get; set; } = new();

    public string Status =>
        IsOverridden && FolderFound ? $"{Bom} · 파일 {Files.Count}개"
        : IsOverridden ? $"{Bom} · 폴더 없음"
        : MergedCell ? "셀 병합됨 — 클릭해서 직접 지정"
        : !BomFound ? "BOM 조회 실패 — 클릭해서 직접 지정"
        : !FolderFound ? $"{Bom} · 폴더 없음"
        : $"{Bom} · 파일 {Files.Count}개";

    /// <summary>직접 지정한 경우 상태 뒤에 표시.</summary>
    public string OverrideTag => IsOverridden ? "직접 지정" : "";

    public Severity StatusSev =>
        !BomFound || !FolderFound ? Severity.Error : Severity.Pass;

    public event PropertyChangedEventHandler? PropertyChanged;

    void NotifyAll()
    {
        foreach (var n in new[] { nameof(Bom), nameof(BomFound), nameof(FolderName),
                                  nameof(Status), nameof(StatusSev), nameof(IsOverridden),
                                  nameof(OverrideTag), nameof(FolderFound) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

/// <summary>
/// (BOM, Ver) 쌍으로 묶인 4-N 블록 하나.
/// BOM 이 같아도 버전이 다르면 별개 블록이 된다.
/// </summary>
public sealed class ProductVersionBlock
{
    public string Bom { get; init; } = "";
    public string Version { get; init; } = "";
    public List<string> Models { get; init; } = new();
    public List<string> Files { get; init; } = new();
    public string Cpu { get; set; } = "";

    public string FolderName => $"{Bom}_{Version}";
}

// ── 변환 규칙 ──────────────────────────────────────────────

public enum TransformKind
{
    ModelList,       // T1  1. 모델이름
    ProductVersion,  // T2  4. 제품버전 재생성
    ListFilter,      // T3  6. 제조환경 모델 리스트 필터
    SwVersionLine,   // T4  6-2 의 S/W Version 절차 줄
    DropBlock        // T5  단락 삭제 (6-1 구미공정 등)
}

/// <summary>A / B / C 3택 1.</summary>
public enum FilterMode
{
    /// <summary>A. 원본 그대로. 등록 모델이 없으면 로그에 경고만.</summary>
    KeepAll,
    /// <summary>B. 등록한 모델만 남긴다. 내 모델이 없는 그룹은 통째로 삭제.</summary>
    OnlyRegistered,
    /// <summary>C. 무관 그룹만 삭제. 남은 그룹의 모델 나열은 손대지 않는다.</summary>
    DropUnrelatedGroups
}

public sealed class TransformRule
{
    public int No { get; set; }
    public TransformKind Kind { get; set; }

    [JsonIgnore] public string Bid { get; set; } = "";
    [JsonIgnore] public string Num { get; set; } = "";
    [JsonIgnore] public string Title { get; set; } = "";

    /// <summary>재연결 기준. 번호는 신뢰하지 않는다.</summary>
    public string AnchorKeyword { get; set; } = "";
    public string? AnchorNumberAtAuthoring { get; set; }

    public FilterMode Filter { get; set; } = FilterMode.KeepAll;

    [JsonIgnore] public string KindLabel => Kind switch
    {
        TransformKind.ModelList      => "모델 리스트 교체",
        TransformKind.ProductVersion => "제품버전 재생성",
        TransformKind.ListFilter     => "모델 리스트 필터",
        TransformKind.SwVersionLine  => "S/W Version 줄 재작성",
        TransformKind.DropBlock      => "단락 삭제",
        _ => Kind.ToString()
    };

    [JsonIgnore] public string ShortId => Kind switch
    {
        TransformKind.ModelList      => "T1",
        TransformKind.ProductVersion => "T2",
        TransformKind.ListFilter     => "T3",
        TransformKind.SwVersionLine  => "T4",
        TransformKind.DropBlock      => "T5",
        _ => "T?"
    };

    [JsonIgnore] public string Detail => Kind switch
    {
        TransformKind.ListFilter => Filter switch
        {
            FilterMode.KeepAll             => "A · 원본 그대로",
            FilterMode.OnlyRegistered      => "B · 등록 모델만 남김",
            FilterMode.DropUnrelatedGroups => "C · 무관 그룹만 삭제",
            _ => ""
        },
        _ => "기본 설정"
    };

    [JsonIgnore] public string AnchorNote => $"앵커 \"{AnchorKeyword}\" · 번호 무시";
}

/// <summary>문서 전역 설정. 규칙마다 반복 입력하지 않는다.</summary>
public sealed class WorkspaceSettings
{
    public string FirmwareRoot { get; set; } = "";
    public string ExcelPath { get; set; } = "";
    public string SheetName { get; set; } = "Sheet1";
    public string MatchColumn { get; set; } = "G";
    public string ValueColumn { get; set; } = "K";
    public bool SplitCell { get; set; } = true;

    /// <summary>모든 4-N 블록의 CPU 줄. 기본값을 두고 수정 가능하게.</summary>
    public string Cpu { get; set; } = "ESP32-S3R16 (ESPRESSIF)";

    /// <summary>F/W 괄호 맨 앞에 이 순서로 고정 배치.</summary>
    public string FixedFilePrefixes { get; set; } =
        "bootloader, partition-table, lgha_new_standard_rcu";
}

/// <summary>저장/공유되는 생성 규칙 세트.</summary>
/// <summary>
/// 저장/공유되는 변환 규칙 세트.
///
/// 규칙만 담는다. 모델 그룹 · 경로 · 엑셀 설정은 저장하지 않는다.
/// (경로 · 사전은 AppSettings 가 %APPDATA% 에 따로 기억한다)
///
/// UseEnglish 는 규칙 재연결에 필요해서 남긴다. 규칙이 영문 단락에도
/// 미러링되므로, 불러올 때 영문 여부를 알아야 앵커를 제대로 찾는다.
/// </summary>
public sealed class GeneratorSet
{
    public string Schema { get; set; } = "sibang-generator/2";
    public DateTime Saved { get; set; } = DateTime.Now;
    public bool UseEnglish { get; set; }
    public string? BoundaryHint { get; set; }
    public List<TransformRule> Rules { get; set; } = new();
}

/// <summary>생성 결과와 그 과정에서 나온 경고.</summary>
public sealed class GenerationResult
{
    public string[] Lines { get; init; } = Array.Empty<string>();
    public List<Issue> Log { get; init; } = new();

    public int ErrorCount => Log.Count(x => x.Sev == Severity.Error);
    public int WarnCount => Log.Count(x => x.Sev == Severity.Warning);
}
