using System.Text.RegularExpressions;
using SibangGenerator.Models;
using SibangGenerator.Services.Transforms;

namespace SibangGenerator.Services;

/// <summary>
/// 기존 시방 + 등록 모델 + 변환 규칙  →  새 시방.
///
/// 파이프라인:
///   1) 변환기 적용 (블록 단위, 원본 순서 유지)
///   2) 번호 재부여 (4-N, 6-N 등)
///
/// 변환기를 추가하려면 Transforms/ 에 ITransform 구현을 만들고
/// 아래 _transforms 에 등록하기만 하면 된다.
/// </summary>
public sealed class SpecGenerator
{
    readonly SpecParser _parser;
    readonly WorkspaceSettings _ws;

    /// <summary>변환기 레지스트리. 새 변환기는 여기에만 추가.</summary>
    static readonly Dictionary<TransformKind, ITransform> _transforms = new()
    {
        [TransformKind.ModelList]      = new ModelListTransform(),
        [TransformKind.ProductVersion] = new ProductVersionTransform(),
        [TransformKind.ListFilter]     = new ListFilterTransform(),
        [TransformKind.SwVersionLine]  = new SwVersionTransform(),
        [TransformKind.DropBlock]      = new DropBlockTransform(),
    };

    public SpecGenerator(SpecParser parser, WorkspaceSettings ws)
    {
        _parser = parser;
        _ws = ws;
    }

    public GenerationResult Generate(
        IReadOnlyList<VersionGroup> groups,
        IReadOnlyList<TransformRule> rules)
    {
        using var bom = new BomLookup(_ws);
        var fw = new FirmwareScanner(_ws);

        var log = new List<Issue>();

        // 모든 모델이 BOM 을 직접 지정했다면 엑셀이 없어도 문제없다.
        bool allOverridden = groups.All(g => g.Models.All(m => g.BomOverrides.ContainsKey(m)));

        if (!bom.Ready)
            log.Add(new Issue
            {
                Sev = allOverridden ? Severity.Info : Severity.Error,
                Line = 0,
                Msg = allOverridden
                    ? "엑셀 없이 진행 · 모든 모델의 BOM 을 직접 지정했습니다"
                    : (bom.Error ?? "엑셀 오류")
            });

        if (!fw.Ready)
            log.Add(new Issue { Sev = Severity.Error, Line = 0,
                Msg = $"펌웨어 루트를 찾을 수 없습니다: {_ws.FirmwareRoot}" });

        // ── 모델 해석 ──────────────────────────────
        var models = ModelResolver.Resolve(groups, bom, fw);
        var blocks = ModelResolver.BuildBlocks(models, _ws);

        var ctx = new TransformContext
        {
            Parser = _parser,
            Settings = _ws,
            Groups = groups.ToList(),
            Models = models,
            Blocks = blocks,
            Registered = ModelResolver.AllModelNames(groups)
        };
        ctx.Log.AddRange(log);

        // 고정 파일 누락 확인
        foreach (var b in blocks)
            foreach (var miss in fw.MissingFixed(b.Bom, b.Version))
                ctx.Error(0, $"{b.FolderName} · 고정 파일 {miss} 없음");

        // ── 1) 변환기 적용 ─────────────────────────
        // 영문 경계 줄의 텍스트를 미리 기억해둔다 (줄 번호는 변환 후 달라진다)
        string? boundary = (_parser.UseEnglish && _parser.EnglishAt >= 0)
            ? _parser.Lines[_parser.EnglishAt]
            : null;

        var lines = ApplyTransforms(rules, ctx);

        // ── 2) 번호 재부여 ─────────────────────────
        int enStart = FindEnglishStart(lines, boundary);
        lines = Renumber(lines, enStart);

        return new GenerationResult { Lines = lines.ToArray(), Log = ctx.Log };
    }

    // ═══ 1) 변환기 적용 ═══════════════════════════

    /// <summary>
    /// 규칙이 걸린 블록은 변환기를 통과시키고, 나머지 줄은 원본 그대로 흘려보낸다.
    ///
    /// 영문 미러링: 규칙은 한글 블록에만 걸면 된다.
    /// 여기서 짝지어진 영문 블록에도 같은 규칙을 자동으로 적용한다.
    /// (한↔영 1:1 대응 전제. 영문 체크박스가 꺼져 있으면 짝이 없으므로 아무 일도 안 한다)
    ///
    /// 블록이 겹치는 경우(부모/자식)를 피하기 위해, 시작줄 기준으로 정렬한 뒤
    /// 겹치지 않는 것만 채택한다.
    /// </summary>
    List<string> ApplyTransforms(IReadOnlyList<TransformRule> rules, TransformContext ctx)
    {
        var jobs = new List<(Block B, TransformRule R, bool IsEn)>();

        foreach (var r in rules)
        {
            var b = _parser.FindByAnchor(r.AnchorKeyword);
            if (b is null)
            {
                ctx.Error(0, $"앵커 \"{r.AnchorKeyword}\" 단락을 찾지 못해 건너뜀");
                continue;
            }
            jobs.Add((b, r, false));

            // 짝지어진 영문 블록에 같은 규칙을 자동 적용
            if (_parser.Pairs.TryGetValue(b.Bid, out var en))
                jobs.Add((en, r, true));
        }

        // 시작줄 순 정렬 + 겹침 제거 (앞선 것 우선)
        jobs.Sort((a, b) => a.B.From.CompareTo(b.B.From));

        var accepted = new List<(Block B, TransformRule R, bool IsEn)>();
        int guard = -1;
        foreach (var j in jobs)
        {
            if (j.B.From <= guard)
            {
                ctx.Warn(j.B.From, $"{j.B.Num} {j.B.Title} · 앞선 규칙과 범위가 겹쳐 건너뜀");
                continue;
            }
            accepted.Add(j);
            guard = j.B.To - 1;
        }

        int enCount = accepted.Count(x => x.IsEn);
        if (enCount > 0) ctx.Info(0, $"영문 블록 {enCount}개에 규칙 자동 적용");

        // 원본을 훑으며 조립
        var outp = new List<string>();
        int cursor = 0;

        foreach (var (b, r, _) in accepted)
        {
            for (int i = cursor; i < b.From; i++) outp.Add(_parser.Lines[i]);

            var src = _parser.Lines[b.From..b.To];

            if (_transforms.TryGetValue(r.Kind, out var t))
                outp.AddRange(t.Apply(r, b, src, ctx));
            else
            {
                ctx.Error(b.From, $"알 수 없는 변환기: {r.Kind}");
                outp.AddRange(src);
            }

            cursor = b.To;
        }

        for (int i = cursor; i < _parser.Lines.Length; i++) outp.Add(_parser.Lines[i]);

        return outp;
    }

    // ═══ 2) 번호 재부여 ═══════════════════════════

    static readonly Regex RxNumbered = new(@"^([ \t]*)(\d+)(-(\d+))?(\s*[.)])", RegexOptions.Compiled);

    /// <summary>
    /// 단락 삭제 후 번호를 다시 매긴다.
    ///
    /// - "N." 최상위 섹션은 원본 번호를 유지한다 (0. ~ 9. 는 고정 의미)
    /// - "N-M)" 하위 번호는 같은 부모 안에서 1부터 다시 매긴다
    ///
    /// 예) 6-1) 구미공정 삭제 → 6-2) JIG 가 6-1) 이 된다
    ///
    /// 한↔영 구간은 카운터를 따로 센다.
    /// 그러지 않으면 영문 4-1 이 4-6 부터 시작한다.
    ///
    /// 절차 번호("1)", "2)")는 건드리지 않는다. 그건 변환기가 원본 그대로 옮기므로
    /// 이미 연속이고, 여기서 손대면 오히려 깨진다.
    /// </summary>
    /// <param name="englishStart">영문 구간이 시작되는 줄 인덱스. 없으면 -1.</param>
    static List<string> Renumber(List<string> lines, int englishStart)
    {
        // (구간, 들여쓰기, 부모번호) 별 카운터
        var counter = new Dictionary<string, int>();

        for (int i = 0; i < lines.Count; i++)
        {
            var m = RxNumbered.Match(lines[i]);
            if (!m.Success) continue;

            // "N-M)" 형태만 대상
            if (!m.Groups[3].Success) continue;

            string indent = m.Groups[1].Value;
            string parent = m.Groups[2].Value;
            string tail = m.Groups[5].Value;
            string zone = (englishStart >= 0 && i >= englishStart) ? "EN" : "KO";

            string key = $"{zone}|{indent.Length}|{parent}";
            counter.TryGetValue(key, out int n);
            n++;
            counter[key] = n;

            string rest = lines[i][m.Length..];
            lines[i] = $"{indent}{parent}-{n}{tail}{rest}";
        }
        return lines;
    }

    /// <summary>
    /// 변환 후 영문 구간이 몇 번째 줄에서 시작하는지 다시 찾는다.
    /// 줄이 추가·삭제되었으므로 원본 인덱스는 못 쓴다.
    /// 원본 경계 줄의 텍스트를 그대로 찾는다.
    /// </summary>
    static int FindEnglishStart(List<string> lines, string? boundaryText)
    {
        if (string.IsNullOrWhiteSpace(boundaryText)) return -1;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Trim() == boundaryText.Trim()) return i;
        return -1;
    }
}
