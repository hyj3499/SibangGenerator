using SibangGenerator.Models;

namespace SibangGenerator.Services.Transforms;

/// <summary>
/// T5. 단락을 통째로 삭제한다.
///
/// 용도: "6-1) 구미 재작업 공정" 이 필요 없는 시방.
/// 삭제 후 남은 "6-2) JIG" 는 번호 재부여 패스에서 자동으로 6-1) 이 된다.
/// </summary>
public sealed class DropBlockTransform : ITransform
{
    public TransformKind Kind => TransformKind.DropBlock;

    public List<string> Apply(TransformRule rule, Block block,
                              IReadOnlyList<string> src, TransformContext ctx)
    {
        ctx.Info(block.From, $"단락 삭제 · {block.Num} {block.Title} ({src.Count}줄)");
        return new List<string>();   // 빈 리스트 = 삭제
    }
}
