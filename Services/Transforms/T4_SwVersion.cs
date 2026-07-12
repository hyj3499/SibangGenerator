using System.Text.RegularExpressions;
using SibangGenerator.Models;

namespace SibangGenerator.Services.Transforms;

/// <summary>
/// T4. 6-2 JIG 절차의 "S/W Version" 줄을 등록 버전에 맞게 다시 쓴다.
///
/// 버전이 1개일 때 — 원본 형태 유지
///       3) S/W Version : 1.00.6a 확인
///
/// 버전이 2개 이상일 때 — 하위 항목으로 펼침
///       3) S/W Version 확인
///           - 1.00.7a 확인 모델 : PREMTBB20.ENCXUAEC, PREMTB200.ENCXCOM
///           - 1.00.8a 확인 모델 : PREMTB200.AKMC, PREMTB200.AKM
///
/// 버전 그룹의 등록 순서가 그대로 출력 순서가 된다.
/// 영문은 Check S/W Version 형태로 나가며, 문구는 아래 상수로 분리해 두었다.
/// </summary>
public sealed class SwVersionTransform : ITransform
{
    public TransformKind Kind => TransformKind.SwVersionLine;

    // ── 문구. 필요하면 여기만 고치면 된다 ──────────────
    const string KoSingle    = "S/W Version : {0} 확인";
    const string KoMultiHead = "S/W Version 확인";
    const string KoMultiItem = "- {0} 확인 모델 : {1}";

    const string EnSingle    = "Check S/W Version: {0}";
    const string EnMultiHead = "Check S/W Version";
    const string EnMultiItem = "- {0} : {1}";

    /// <summary>"      3) S/W Version : 1.00.6a 확인" 을 잡는다.</summary>
    static readonly Regex RxSwLine = new(
        @"^(\s*)(\d+\s*\))\s*(?:S/W Version|SW 버전)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>기존 하위 항목 ("- 1.00.8a 확인")</summary>
    static readonly Regex RxSubItem = new(@"^\s*-\s*\d\.\d{2}\.\d+[a-z]?", RegexOptions.Compiled);

    public List<string> Apply(TransformRule rule, Block block,
                              IReadOnlyList<string> src, TransformContext ctx)
    {
        var outp = new List<string>();
        bool isEnglish = IsEnglishZone(block, ctx);
        bool replaced = false;

        for (int i = 0; i < src.Count; i++)
        {
            var m = RxSwLine.Match(src[i]);
            if (!m.Success) { outp.Add(src[i]); continue; }

            string indent = m.Groups[1].Value;
            string number = m.Groups[2].Value;

            // 기존 줄 + 딸린 하위 항목을 모두 소비
            int j = i + 1;
            while (j < src.Count && RxSubItem.IsMatch(src[j])) j++;

            outp.AddRange(Build(indent, number, isEnglish, ctx));
            i = j - 1;
            replaced = true;
        }

        if (!replaced)
            ctx.Warn(block.From, $"\"{block.Title}\" 에서 S/W Version 줄을 찾지 못했습니다");
        else
            ctx.Info(block.From,
                ctx.SingleVersion
                    ? $"S/W Version 줄 · 단일 버전 {ctx.TheVersion}"
                    : $"S/W Version 줄 · {ctx.Groups.Count}개 버전으로 펼침");

        return outp;
    }

    /// <summary>새 S/W Version 줄(들)을 만든다.</summary>
    static List<string> Build(string indent, string number, bool en, TransformContext ctx)
    {
        var lines = new List<string>();

        if (ctx.SingleVersion)
        {
            string fmt = en ? EnSingle : KoSingle;
            lines.Add($"{indent}{number} {string.Format(fmt, ctx.TheVersion)}");
            return lines;
        }

        // 하위 항목 들여쓰기: 번호 줄보다 4칸 더
        string sub = indent + new string(' ', 4);

        lines.Add($"{indent}{number} {(en ? EnMultiHead : KoMultiHead)}");
        foreach (var g in ctx.Groups)
        {
            if (g.Models.Count == 0) continue;
            string fmt = en ? EnMultiItem : KoMultiItem;
            lines.Add($"{sub}{string.Format(fmt, g.Version, string.Join(", ", g.Models))}");
        }
        return lines;
    }

    /// <summary>블록이 영문 구간에 있는가?</summary>
    static bool IsEnglishZone(Block block, TransformContext ctx) =>
        ctx.Parser.UseEnglish && ctx.Parser.EnglishAt >= 0 && block.From >= ctx.Parser.EnglishAt;
}
