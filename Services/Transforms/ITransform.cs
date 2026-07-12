using SibangGenerator.Models;

namespace SibangGenerator.Services.Transforms;

/// <summary>
/// 모든 변환기가 공유하는 문맥.
/// 여기에만 상태가 있고 변환기 자체는 무상태다.
/// </summary>
public sealed class TransformContext
{
    public required SpecParser Parser { get; init; }
    public required WorkspaceSettings Settings { get; init; }
    public required List<VersionGroup> Groups { get; init; }
    public required List<ResolvedModel> Models { get; init; }
    public required List<ProductVersionBlock> Blocks { get; init; }

    /// <summary>등록된 모델명 집합. 필터 판정용.</summary>
    public required HashSet<string> Registered { get; init; }

    /// <summary>생성 과정에서 쌓이는 로그.</summary>
    public List<Issue> Log { get; } = new();

    public void Warn(int line, string msg) =>
        Log.Add(new Issue { Sev = Severity.Warning, Line = line, Msg = msg });

    public void Error(int line, string msg) =>
        Log.Add(new Issue { Sev = Severity.Error, Line = line, Msg = msg });

    public void Info(int line, string msg) =>
        Log.Add(new Issue { Sev = Severity.Info, Line = line, Msg = msg });

    /// <summary>줄에서 사전에 등록된 모델명을 찾는다(최장일치).</summary>
    public List<string> MatchModels(string line) => Parser.MatchModels(line);

    /// <summary>버전이 하나뿐인가? S/W Version 줄 형태를 가른다.</summary>
    public bool SingleVersion => Groups.Select(g => g.Version).Distinct().Count() <= 1;

    public string TheVersion => Groups.FirstOrDefault()?.Version ?? "";
}

/// <summary>
/// 변환기 하나. 원본 블록의 줄들을 받아 새 줄들을 돌려준다.
/// 빈 리스트를 돌려주면 그 블록은 삭제된다.
/// </summary>
public interface ITransform
{
    TransformKind Kind { get; }

    /// <param name="rule">사용자가 지정한 설정</param>
    /// <param name="block">원본 블록 (앵커로 찾은 것)</param>
    /// <param name="src">블록에 해당하는 원본 줄들 (헤더 포함)</param>
    List<string> Apply(TransformRule rule, Block block, IReadOnlyList<string> src, TransformContext ctx);
}

/// <summary>변환기 공용 헬퍼.</summary>
public static class TransformUtil
{
    /// <summary>줄의 선행 공백을 그대로 돌려준다. 들여쓰기 보존용.</summary>
    public static string Indent(string line) =>
        new(line.TakeWhile(c => c == ' ' || c == '\t').ToArray());

    /// <summary>
    /// "- MODEL_A, MODEL_B, MODEL_C" 같은 줄에서 모델 나열 부분만 갈아끼운다.
    /// 접두어(- 또는 공백)와 뒤에 붙은 것들은 보존한다.
    /// </summary>
    public static string ReplaceModelList(string line, IEnumerable<string> models)
    {
        string indent = Indent(line);
        string body = line[indent.Length..];

        // "- " 같은 불릿 접두어 보존
        string bullet = "";
        if (body.StartsWith("- ")) { bullet = "- "; body = body[2..]; }
        else if (body.StartsWith("-")) { bullet = "- "; body = body[1..].TrimStart(); }

        return indent + bullet + string.Join(", ", models);
    }

    /// <summary>이 줄이 모델 나열 줄인가? (모델이 하나라도 있고, 절차 번호가 아님)</summary>
    public static bool IsModelListLine(string line, TransformContext ctx)
    {
        string t = line.TrimStart();
        if (t.Length == 0) return false;
        // "1) F/W Copy" 같은 절차 줄 제외
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+\s*[.)]")) return false;
        return ctx.MatchModels(line).Count > 0;
    }
}
