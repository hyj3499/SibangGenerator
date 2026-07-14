using System.IO;
using ClosedXML.Excel;
using SibangGenerator.Models;

namespace SibangGenerator.Services;

/// <summary>
/// 엑셀에서 모델명 → BOM 을 조회한다.
/// 검증툴의 S4(엑셀 값 대조) 로직과 동일한 규칙을 쓴다.
///
/// 핵심: 한 셀에 여러 모델이 줄바꿈으로 들어있을 수 있으므로 분해한 뒤
/// 정확일치로 찾는다. 그래야 ENCXUAEC 와 ENCXUAECRC 가 섞이지 않는다.
/// </summary>
public sealed class BomLookup : IDisposable
{
    readonly XLWorkbook? _wb;
    readonly IXLWorksheet? _sheet;
    readonly int _valueCol, _lastRow;
    readonly int[] _matchCols; // 여러 열 입력 가능
    readonly bool _splitCell;

    public bool Ready => _sheet is not null;
    public string? Error { get; }

    public BomLookup(WorkspaceSettings ws)
    {
        _splitCell = ws.SplitCell;
        _matchCols = (ws.MatchColumn ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ColumnIndex)
            .ToArray();
        _valueCol = ColumnIndex(ws.ValueColumn);

        if (string.IsNullOrWhiteSpace(ws.ExcelPath))
        { Error = "엑셀 경로가 지정되지 않았습니다."; return; }

        if (!File.Exists(ws.ExcelPath))
        { Error = $"엑셀 파일을 찾을 수 없습니다: {ws.ExcelPath}"; return; }

        if (_matchCols.Length == 0)
        { Error = "엑셀 열이 지정되지 않았습니다."; return; }

        try
        {
            _wb = new XLWorkbook(ws.ExcelPath);
            _sheet = _wb.Worksheets.FirstOrDefault(x => x.Name == ws.SheetName)
                     ?? _wb.Worksheet(1);
            _lastRow = _sheet.LastRowUsed()?.RowNumber() ?? 0;
        }
        catch (Exception ex)
        {
            Error = $"엑셀을 열 수 없습니다: {ex.Message}";
        }
    }

    /// <summary>모델명으로 BOM 을 찾는다. 못 찾으면 null.</summary>
    public string? Find(string model)
    {
        if (_sheet is null) return null;

        for (int r = 1; r <= _lastRow; r++)
        {
            foreach (int matchCol in _matchCols)
            {
                string cell = _sheet.Cell(r, matchCol).GetString();
                if (cell.Length == 0) continue;

                var candidates = _splitCell
                    ? cell.Split(new[] { '\r', '\n', ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : new[] { cell.Trim() };
                if (candidates.Any(c => string.Equals(c, model, StringComparison.Ordinal)))
                {
                    string bom = _sheet.Cell(r, _valueCol).GetString().Trim();
                    return bom.Length > 0 ? bom : null; // 일치하는 행을 찾았으므로 즉시 반환
                }
            }
        }
        return null;
    }


    /// <summary>엑셀 열문자 → 1-based 인덱스 (G → 7).</summary>
    static int ColumnIndex(string col)
    {
        int n = 0;
        foreach (char c in (col ?? "").ToUpperInvariant())
            if (c is >= 'A' and <= 'Z') n = n * 26 + (c - 'A' + 1);
        return Math.Max(n, 1);
    }

    public void Dispose() => _wb?.Dispose();
}

/// <summary>
/// 펌웨어 루트 아래의 {BOM}_{Ver} 폴더를 읽는다.
/// </summary>
public sealed class FirmwareScanner
{
    readonly string _root;
    readonly string[] _fixedPrefixes;

    /// <summary>설정이 비어 있어도 이 순서는 반드시 지킨다.</summary>
    public static readonly string[] DefaultFixedPrefixes =
        { "bootloader", "partition-table", "lgha_new_standard_rcu" };

    public bool Ready => Directory.Exists(_root);
    public string Root => _root;

    public FirmwareScanner(WorkspaceSettings ws)
    {
        _root = ws.FirmwareRoot ?? "";

        // UI 초기화 순서 때문에 빈 문자열이 들어올 수 있다. 그때는 기본값을 쓴다.
        var parsed = (ws.FixedFilePrefixes ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _fixedPrefixes = parsed.Length > 0 ? parsed : DefaultFixedPrefixes;
    }

    /// <summary>
    /// 루트 아래 폴더 이름에서 버전을 수집한다.
    /// OHS3165F_1.00.7a → 1.00.7a
    /// ② 단계 버전 콤보박스의 선택지가 된다.
    /// </summary>
    public List<string> DiscoverVersions()
    {
        if (!Ready) return new();

        var versions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(_root))
        {
            string name = Path.GetFileName(dir);
            int us = name.IndexOf('_');
            if (us > 0 && us < name.Length - 1)
                versions.Add(name[(us + 1)..]);
        }
        return versions.OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 폴더 이름에서 BOM 을 수집한다.
    /// OHS3165F_1.00.7a → OHS3165F
    ///
    /// 특정 버전을 주면 그 버전의 폴더가 있는 BOM 만 돌려준다.
    /// BOM 직접 지정 대화상자의 후보 목록이 된다.
    /// </summary>
    public List<string> DiscoverBoms(string? version = null)
    {
        if (!Ready) return new();

        var boms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(_root))
        {
            string name = Path.GetFileName(dir);
            int us = name.IndexOf('_');
            if (us <= 0) continue;

            string bom = name[..us];
            string ver = name[(us + 1)..];

            if (version is null || ver == version) boms.Add(bom);
        }
        return boms.OrderBy(b => b, StringComparer.Ordinal).ToList();
    }

    public bool FolderExists(string bom, string version) =>
        Ready && Directory.Exists(Path.Combine(_root, $"{bom}_{version}"));

    /// <summary>
    /// 폴더의 파일 목록을 F/W 줄에 적을 순서로 돌려준다.
    ///
    /// 1) 고정 접두어(bootloader, partition-table, lgha_new_standard_rcu)를
    ///    설정된 순서대로 먼저
    /// 2) 나머지 파일 전부를 파일명 오름차순으로
    ///
    /// npprj 로 한정하지 않는다. 고정 3종만 있을 수도 있다.
    /// </summary>
    public List<string> ReadFiles(string bom, string version)
    {
        string folder = Path.Combine(_root, $"{bom}_{version}");
        if (!Directory.Exists(folder)) return new();

        // OS 순서는 보장이 없으므로 항상 정렬해서 결정적으로 만든다
        var all = Directory.GetFiles(folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var ordered = new List<string>();

        foreach (var prefix in _fixedPrefixes)
        {
            var hit = all.FirstOrDefault(f =>
                f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) { ordered.Add(hit); all.Remove(hit); }
        }

        ordered.AddRange(all);   // 나머지 전부, 이미 오름차순
        return ordered;
    }

    /// <summary>고정 3종 중 폴더에 없는 것.</summary>
    public List<string> MissingFixed(string bom, string version)
    {
        var files = ReadFiles(bom, version);
        return _fixedPrefixes
            .Where(p => !files.Any(f => f.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
