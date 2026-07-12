using System.Text.RegularExpressions;
using SibangGenerator.Models;

namespace SibangGenerator.Services.Transforms;

/// <summary>
/// T2. "4. 제품버전" 의 하위 4-N 블록을 전부 버리고 새로 만든다.
///
/// 그룹 규칙:
///   같은 BOM + 같은 버전  → 한 블록에 모델 나열
///   같은 BOM + 다른 버전  → 별개 블록
///
/// 블록 순서는 ② 단계 입력 순서를 따른다 (ModelResolver.BuildBlocks 에서 결정).
///
/// 출력 형태:
///     4-1) MODEL_A, MODEL_B
///         - F/W : OHS3165F_1.00.7a.zip (bootloader_*.bin, partition-table_*.bin, lgha_*.bin, *.npprj)
///         - CPU : ESP32-S3R16 (ESPRESSIF)
///         - BOM : OHS3165F
///         - Ver : 1.00.7a
/// </summary>
public sealed class ProductVersionTransform : ITransform
{
    public TransformKind Kind => TransformKind.ProductVersion;

    // 들여쓰기는 원본에서 학습한다. 못 찾으면 이 기본값.
    const string DefaultBlockIndent = "    ";
    const string DefaultFieldIndent = "        ";

    static readonly Regex RxSubHead = new(@"^(\s*)\d+-\d+\s*\)", RegexOptions.Compiled);
    static readonly Regex RxField   = new(@"^(\s*)-\s*(?:F/W|CPU|BOM|Ver)\s*:", RegexOptions.Compiled);

    public List<string> Apply(TransformRule rule, Block block,
                              IReadOnlyList<string> src, TransformContext ctx)
    {
        var outp = new List<string>();

        // 1) 헤더("4. 제품버전 :")는 그대로 유지
        if (src.Count > 0) outp.Add(src[0]);

        // 2) 원본에서 들여쓰기 학습
        var (blockIndent, fieldIndent) = LearnIndents(src);

        // 3) 사용 불가 모델 경고
        foreach (var m in ctx.Models.Where(m => !m.BomFound))
            ctx.Error(block.From, $"{m.Name} · BOM 없음 (② 조회 결과에서 직접 지정 가능) — 제외됨");
        foreach (var m in ctx.Models.Where(m => m.BomFound && !m.FolderFound))
            ctx.Error(block.From, $"{m.Name} · 폴더 {m.FolderName} 없음 — 제외됨");

        if (ctx.Blocks.Count == 0)
        {
            ctx.Error(block.From, "생성할 제품버전 블록이 하나도 없습니다");
            return outp;
        }

        // 4) 블록 생성. 번호는 1부터 새로 매긴다.
        //    부모 번호(4)는 원본 헤더에서 가져온다.
        string parentNum = ExtractParentNumber(src.Count > 0 ? src[0] : "") ?? block.Num;

        int n = 1;
        foreach (var b in ctx.Blocks)
        {
            outp.Add($"{blockIndent}{parentNum}-{n}) {string.Join(", ", b.Models)}");
            outp.Add($"{fieldIndent}- F/W : {BuildFwLine(b, ctx)}");
            outp.Add($"{fieldIndent}- CPU : {b.Cpu}");
            outp.Add($"{fieldIndent}- BOM : {b.Bom}");
            outp.Add($"{fieldIndent}- Ver : {b.Version}");
            n++;
        }

        ctx.Info(block.From, $"제품버전 {ctx.Blocks.Count}개 블록 생성 ({parentNum}-1 ~ {parentNum}-{n - 1})");
        return outp;
    }

    /// <summary>
    /// F/W 줄 조립.
    ///   {BOM}_{Ver}.zip (파일1, 파일2, ...)
    /// 파일 순서는 FirmwareScanner 가 이미 정해둔 것 그대로.
    /// (고정 3종 먼저, 나머지 파일명 오름차순. npprj 로 한정하지 않음)
    /// </summary>
    static string BuildFwLine(ProductVersionBlock b, TransformContext ctx)
    {
        if (b.Files.Count == 0)
        {
            ctx.Error(0, $"{b.FolderName} 폴더가 비어 있습니다");
            return $"{b.FolderName}.zip ()";
        }
        return $"{b.FolderName}.zip ({string.Join(", ", b.Files)})";
    }

    /// <summary>"4. 제품버전 :" → "4"</summary>
    static string? ExtractParentNumber(string headerLine)
    {
        var m = Regex.Match(headerLine, @"^\s*(\d+)\s*[.)]");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>원본의 4-N 줄과 필드 줄에서 들여쓰기를 학습한다.</summary>
    static (string Block, string Field) LearnIndents(IReadOnlyList<string> src)
    {
        string block = DefaultBlockIndent, field = DefaultFieldIndent;

        foreach (var line in src)
        {
            var b = RxSubHead.Match(line);
            if (b.Success) { block = b.Groups[1].Value; break; }
        }
        foreach (var line in src)
        {
            var f = RxField.Match(line);
            if (f.Success) { field = f.Groups[1].Value; break; }
        }
        return (block, field);
    }
}
