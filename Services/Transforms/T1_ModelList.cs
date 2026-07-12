using System.Text.RegularExpressions;
using SibangGenerator.Models;

namespace SibangGenerator.Services.Transforms;

/// <summary>
/// T1. "1. 모델이름 : A, B, C" 의 모델 나열을 등록 모델로 통째 교체.
/// 영문 "1. Model name : ..." 도 같은 규칙으로 처리된다
/// (영문 블록에 별도 규칙을 걸면 됨).
///
/// 콜론 앞부분(라벨)은 그대로 두고 뒤만 바꾼다.
/// </summary>
public sealed class ModelListTransform : ITransform
{
    public TransformKind Kind => TransformKind.ModelList;

    static readonly Regex RxLabel = new(@"^(\s*\d+\s*[.)]\s*[^:：]*[:：]\s*)", RegexOptions.Compiled);

    public List<string> Apply(TransformRule rule, Block block,
                              IReadOnlyList<string> src, TransformContext ctx)
    {
        var models = ctx.Groups.SelectMany(g => g.Models).ToList();
        var outp = new List<string>(src);

        if (outp.Count == 0) return outp;

        var m = RxLabel.Match(outp[0]);
        if (m.Success)
        {
            // "1. 모델이름 : " + "A, B, C"
            outp[0] = m.Groups[1].Value + string.Join(", ", models);
            ctx.Info(block.From, $"모델 리스트 교체 · {models.Count}개");
        }
        else
        {
            ctx.Warn(block.From, "모델 리스트 줄에서 라벨(콜론)을 찾지 못해 건너뜀");
        }

        return outp;
    }
}
