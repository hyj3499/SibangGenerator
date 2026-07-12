using System.Text.RegularExpressions;
using SibangGenerator.Models;

namespace SibangGenerator.Services.Transforms;

/// <summary>
/// T3. 제조환경 안의 모델 나열을 필터링한다.
///
///   A (KeepAll)              원본 그대로. 등록 모델이 어디에도 없으면 로그 경고.
///   B (OnlyRegistered)       등록 모델만 남긴다. 내 모델이 없는 그룹은 통째 삭제.
///   C (DropUnrelatedGroups)  무관 그룹만 삭제. 남은 그룹의 나열은 손대지 않는다.
///
/// ── 그룹의 정의 ────────────────────────────────────
/// 두 가지 형태가 있다.
///
/// (a) 나열이 불릿 줄에 바로 있는 경우
///       - MODEL_A, MODEL_B, MODEL_C
///         1) F/W Copy
///
/// (b) 불릿 줄은 헤더이고 모델은 그 아래 더 깊은 줄에 있는 경우
///       - UAE향(Region : UAE, ...)          ← 헤더. 모델 없음
///         MODEL_A, MODEL_B                  ← 여기에 모델
///
/// (b)를 한 그룹으로 묶지 않으면 헤더만 살아남아
/// "- 사우디향(...)" 아래가 텅 빈 채로 남는다.
///
/// ── 라벨형 줄 (Wi-Fi / Buzzer) ─────────────────────
///   "- 'Wi-Fi' 표시 모델 : A, B, C"  처럼 콜론 뒤에 모델이 오는 줄은
///   삭제하지 않는다. B 옵션에서 해당 모델이 없으면 나열만 비운다.
///   "- '-' 표시 모델 : ... 표시 제외 모델" 은 설명문이므로 절대 건드리지 않는다.
/// </summary>
public sealed class ListFilterTransform : ITransform
{
    public TransformKind Kind => TransformKind.ListFilter;

    /// <summary>"- 'Wi-Fi' 표시 모델 : " 처럼 라벨 + 콜론 + 모델나열</summary>
    static readonly Regex RxLabeled = new(@"^(\s*-\s*.*?[:：]\s*)(.*)$", RegexOptions.Compiled);

    /// <summary>모델이 아니라 설명이 들어가는 줄. 손대지 않는다.</summary>
    static readonly Regex RxDescriptive = new(
        @"표시 제외 모델|제외 모델|without .* indicators", RegexOptions.Compiled);

    public List<string> Apply(TransformRule rule, Block block,
                              IReadOnlyList<string> src, TransformContext ctx)
    {
        // A 옵션: 원본 그대로. 누락 모델만 확인.
        if (rule.Filter == FilterMode.KeepAll)
        {
            ReportMissing(block, src, ctx);
            return new List<string>(src);
        }

        var outp = new List<string>();
        if (src.Count > 0) outp.Add(src[0]);   // 블록 헤더

        int dropped = 0;

        foreach (var g in SplitGroups(src, ctx))
        {
            // 모델이 하나도 없는 줄 (절차 줄, 순수 텍스트) → 항상 보존
            if (!g.HasModels) { outp.AddRange(g.Lines); continue; }

            string modelLine = g.Lines[g.ModelLineIndex];

            // 설명문 → 손대지 않는다
            if (RxDescriptive.IsMatch(modelLine)) { outp.AddRange(g.Lines); continue; }

            var mine = g.Models.Where(ctx.Registered.Contains).ToList();

            // 라벨형 줄은 절대 삭제하지 않는다. 나열만 조정.
            if (g.IsLabeled)
            {
                var lines = new List<string>(g.Lines);
                if (rule.Filter == FilterMode.OnlyRegistered)
                {
                    var m = RxLabeled.Match(modelLine);
                    lines[g.ModelLineIndex] = m.Groups[1].Value + string.Join(", ", mine);
                }
                // C 옵션은 손대지 않는다
                outp.AddRange(lines);
                continue;
            }

            // 내 모델이 하나도 없는 그룹 → B, C 모두 삭제
            if (mine.Count == 0) { dropped++; continue; }

            if (rule.Filter == FilterMode.OnlyRegistered)
            {
                // B: 나열 줄을 내 모델만으로 다시 씀. 나머지 줄은 그대로.
                var lines = new List<string>(g.Lines);
                lines[g.ModelLineIndex] = RewriteModelLine(modelLine, mine);
                outp.AddRange(lines);
            }
            else
            {
                // C: 그룹을 통째로 원본 그대로
                outp.AddRange(g.Lines);
            }
        }

        if (dropped > 0)
            ctx.Info(block.From, $"{block.Title} · 무관 그룹 {dropped}개 삭제");

        ReportMissing(block, src, ctx);
        return outp;
    }

    /// <summary>들여쓰기와 불릿 접두어를 보존하며 모델 나열만 갈아끼운다.</summary>
    static string RewriteModelLine(string line, IEnumerable<string> models)
    {
        string indent = new(line.TakeWhile(c => c == ' ' || c == '\t').ToArray());
        string body = line[indent.Length..];
        string bullet = body.StartsWith("-") ? "- " : "";
        return indent + bullet + string.Join(", ", models);
    }

    // ── 그룹 분할 ───────────────────────────────────────

    sealed class GroupSlice
    {
        public List<string> Lines { get; } = new();
        /// <summary>Lines 안에서 모델이 처음 등장하는 줄</summary>
        public int ModelLineIndex { get; set; } = -1;
        public List<string> Models { get; } = new();
        public bool IsLabeled { get; set; }
        public bool HasModels => ModelLineIndex >= 0;
        /// <summary>그룹 시작 줄의 들여쓰기 폭</summary>
        public int Indent { get; set; }
    }

    /// <summary>
    /// 블록을 그룹으로 쪼갠다.
    ///
    /// 새 그룹이 시작되는 조건:
    ///   - 불릿 줄("- ...")이 나옴 (헤더일 수도, 나열일 수도 있음)
    ///   - 또는 열린 그룹이 없는데 모델 줄이 나옴
    ///
    /// 그룹이 닫히는 조건:
    ///   - 들여쓰기가 그룹 시작 줄보다 얕거나 같아짐 (불릿 줄 제외 — 다음 그룹 시작)
    ///   - 절차 줄("1) ...")이 같거나 얕은 들여쓰기로 나옴
    ///
    /// 모델은 그룹 안의 모든 줄에서 수집한다.
    /// 그래야 헤더(불릿) + 모델줄(더 깊음) 구조가 한 덩어리가 된다.
    /// </summary>
    static List<GroupSlice> SplitGroups(IReadOnlyList<string> src, TransformContext ctx)
    {
        var groups = new List<GroupSlice>();
        GroupSlice? cur = null;

        for (int i = 1; i < src.Count; i++)   // src[0] 은 블록 헤더
        {
            string line = src[i];
            bool blank = line.Trim().Length == 0;
            int indent = IndentWidth(line);
            bool proc = IsProcedureLine(line);
            bool bullet = IsBulletLine(line);

            var models = proc ? new List<string>() : ctx.MatchModels(line);

            // 그룹 닫기
            //   - 같거나 얕은 들여쓰기의 불릿 → 다음 그룹 시작이므로 닫는다
            //   - 같거나 얕은 들여쓰기의 일반 줄 → 그룹 밖이므로 닫는다
            //   - 같거나 얕은 들여쓰기의 절차 줄 → 닫는다
            if (cur is not null && !blank && indent <= cur.Indent)
                cur = null;

            // 새 그룹 시작
            bool startNew = !blank && !proc && (bullet || (models.Count > 0 && cur is null));

            if (startNew)
            {
                cur = new GroupSlice { Indent = indent };
                cur.Lines.Add(line);
                if (models.Count > 0)
                {
                    cur.ModelLineIndex = 0;
                    cur.Models.AddRange(models);
                }
                groups.Add(cur);
            }
            else if (cur is not null)
            {
                cur.Lines.Add(line);
                if (models.Count > 0)
                {
                    if (cur.ModelLineIndex < 0) cur.ModelLineIndex = cur.Lines.Count - 1;
                    cur.Models.AddRange(models);
                }
            }
            else
            {
                // 그룹 밖의 줄 — 항상 보존
                var lead = new GroupSlice { Indent = indent };
                lead.Lines.Add(line);
                groups.Add(lead);
            }
        }

        // 라벨 여부는 "모델이 실제로 있는 줄" 기준으로 판정한다
        foreach (var g in groups.Where(x => x.HasModels))
            g.IsLabeled = RxLabeled.IsMatch(g.Lines[g.ModelLineIndex]);

        return groups;
    }

    static int IndentWidth(string line) =>
        line.TakeWhile(c => c == ' ' || c == '\t').Sum(c => c == '\t' ? 4 : 1);

    /// <summary>"1) F/W Copy" 같은 절차 줄.</summary>
    static bool IsProcedureLine(string line) =>
        Regex.IsMatch(line.TrimStart(), @"^\d+\s*[.)]");

    /// <summary>
    /// 불릿 줄인가?
    ///
    /// "- MODEL_A"        → 불릿 (새 그룹 시작)
    /// "-  MODEL_A"       → 불릿 (공백 두 칸도 실제 시방에 있다)
    /// "-> TESTMODE ..."  → 불릿 아님. 앞 그룹에 딸린 값이므로 함께 삭제되어야 한다.
    /// </summary>
    static bool IsBulletLine(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith('-') && !t.StartsWith("->");
    }

    // ── 누락 모델 보고 ──────────────────────────────────

    /// <summary>
    /// 등록한 모델이 이 블록 어디에도 안 나타나면 경고.
    /// A / B / C 모두 동일하게 보고하고 진행은 막지 않는다.
    /// </summary>
    static void ReportMissing(Block block, IReadOnlyList<string> src, TransformContext ctx)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in src)
            foreach (var m in ctx.MatchModels(line))
                present.Add(m);

        foreach (var name in ctx.Registered.Where(x => !present.Contains(x)))
            ctx.Warn(block.From, $"{name} · \"{block.Title}\" 에 없음 (옛 시방에 미등장)");
    }
}
