using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SibangGenerator.Models;

namespace SibangGenerator.Services;

/// <summary>
/// 프로그램을 껐다 켜도 유지되는 설정.
///
/// 저장 위치: %APPDATA%\SibangGenerator\settings.json
/// 시나리오 세트(GeneratorSet)와 별개다.
/// 이쪽은 "이 PC 에서 늘 쓰는 값"이고, 저쪽은 "이 시방에 쓰는 규칙"이다.
/// </summary>
public sealed class AppSettings
{
    public string FirmwareRoot { get; set; } = "";
    public string ExcelPath { get; set; } = "";
    public string SheetName { get; set; } = "Sheet1";
    public string MatchColumn { get; set; } = "G";
    public string ValueColumn { get; set; } = "K";
    public bool SplitCell { get; set; } = true;
    public string Cpu { get; set; } = "ESP32-S3R16 (ESPRESSIF)";
    public string FixedFilePrefixes { get; set; } =
        "bootloader, partition-table, lgha_new_standard_rcu";

    /// <summary>모델 사전. 문서마다 자동 수집되지만, 수동 편집분을 기억한다.</summary>
    public List<string> ModelDictionary { get; set; } = new();

    // ── 저장 · 불러오기 ────────────────────────────

    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // 한글이 \uXXXX 로 깨지지 않게
    };

    public static string FilePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SibangGenerator");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    /// <summary>실패해도 예외를 던지지 않는다. 설정이 없으면 기본값.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opt)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>저장 실패는 조용히 무시한다. 설정 저장이 작업을 막으면 안 된다.</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opt), new UTF8Encoding(true));
        }
        catch
        {
            /* 무시 */
        }
    }

    // ── WorkspaceSettings 와 상호 변환 ─────────────

    public WorkspaceSettings ToWorkspace() => new()
    {
        FirmwareRoot = FirmwareRoot,
        ExcelPath = ExcelPath,
        SheetName = SheetName,
        MatchColumn = MatchColumn,
        ValueColumn = ValueColumn,
        SplitCell = SplitCell,
        Cpu = Cpu,
        FixedFilePrefixes = FixedFilePrefixes
    };

    public void CopyFrom(WorkspaceSettings w, IEnumerable<string> dictionary)
    {
        FirmwareRoot = w.FirmwareRoot;
        ExcelPath = w.ExcelPath;
        SheetName = w.SheetName;
        MatchColumn = w.MatchColumn;
        ValueColumn = w.ValueColumn;
        SplitCell = w.SplitCell;
        Cpu = w.Cpu;
        FixedFilePrefixes = w.FixedFilePrefixes;
        ModelDictionary = dictionary.ToList();
    }
}
