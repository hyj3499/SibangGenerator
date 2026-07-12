using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SibangGenerator.Models;

namespace SibangGenerator.Services;

public sealed class SpecParser
{
    public string[] Lines { get; private set; } = Array.Empty<string>();
    public bool UseEnglish { get; set; }
    /// <summary>영문 시작 줄 인덱스. -1이면 지정 안 됨.</summary>
    public int EnglishAt { get; set; } = -1;

    public List<string> Dictionary { get; private set; } = new();
    public List<Block> Blocks { get; private set; } = new();
    public List<string> Versions { get; private set; } = new();

    static readonly Regex RxNumbered = new(@"^([ \t]*)(\d+(?:-\d+)?)\s*[.)]", RegexOptions.Compiled);
    static readonly Regex RxHead     = new(@"^\s*(\d+(?:-\d+)?)\s*[.)]\s*(.*)$", RegexOptions.Compiled);
    static readonly Regex RxVersion  = new(@"\b\d\.\d{2}\.\d+[a-z]?\b", RegexOptions.Compiled);
    static readonly Regex RxAutoDict = new(@"\b(?:PREM[A-Z0-9]+(?:\.[A-Z0-9\-]+)?|C?AKB\d{8}[A-Z\-]*)\b", RegexOptions.Compiled);

    // ── 로드 ───────────────────────────────────────────

    /// <summary>UTF-8(BOM 유무) 우선, 실패하면 CP949로 재시도.</summary>
    public static string ReadTextAuto(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949).GetString(bytes);   // EUC-KR / CP949
        }
    }

    public void Load(string text)
    {
        Lines = text.Replace("\r\n", "\n").Split('\n');
        UseEnglish = false;
        EnglishAt = -1;
        Blocks.Clear();

        Versions = RxVersion.Matches(text).Select(m => m.Value)
                            .Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
        AutoDictionary();
    }

    public void AutoDictionary()
    {
        var text = string.Join("\n", Lines);
        Dictionary = RxAutoDict.Matches(text).Select(m => m.Value)
                               .Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    public void SetDictionary(IEnumerable<string> models) =>
        Dictionary = models.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

    // ── 모델 매칭 (최장일치) ─────────────────────────────

    /// <summary>
    /// 한 줄에서 사전에 등록된 모델명을 찾는다.
    /// 긴 이름부터 매칭하고 매칭 구간을 가려서, ...ENCXUAEC 가 ...ENCXUAECRC 를 잡아먹지 않게 한다.
    /// </summary>
    public List<string> MatchModels(string line)
    {
        var found = new List<string>();
        var buf = new StringBuilder(line);

        foreach (var m in Dictionary.OrderByDescending(x => x.Length))
        {
            var pattern = $@"(?<![A-Za-z0-9.\-]){Regex.Escape(m)}(?![A-Za-z0-9.\-])";
            var rx = new Regex(pattern);
            var s = buf.ToString();
            if (!rx.IsMatch(s)) continue;

            found.Add(m);
            foreach (Match hit in rx.Matches(s))
                for (int i = hit.Index; i < hit.Index + hit.Length; i++)
                    buf[i] = ' ';
        }
        return found;
    }

    // ── 구조 분석 ──────────────────────────────────────

    public List<Issue> Analyze()
    {
        Blocks.Clear();
        int koEnd = (UseEnglish && EnglishAt >= 0) ? EnglishAt : Lines.Length;

        var heads = new List<(string Num, string Title, int Line, int Indent)>();
        for (int i = 0; i < koEnd; i++)
        {
            var m = RxHead.Match(Lines[i]);
            if (!m.Success) continue;
            int indent = Lines[i].TakeWhile(char.IsWhiteSpace).Sum(c => c == '\t' ? 4 : 1);
            heads.Add((m.Groups[1].Value, m.Groups[2].Value.Trim(), i, indent));
        }

        for (int k = 0; k < heads.Count; k++)
        {
            var h = heads[k];
            int end = koEnd;
            for (int j = k + 1; j < heads.Count; j++)
                if (heads[j].Indent <= h.Indent) { end = heads[j].Line; break; }

            var models = new List<string>();
            for (int j = h.Line; j < end; j++)
                foreach (var mm in MatchModels(Lines[j]))
                    if (!models.Contains(mm)) models.Add(mm);

            // 본문 미리보기. 헤더 줄은 이미 위에 나오므로 그 다음 줄부터.
            var preview = new List<PreviewLine>();
            for (int j = h.Line + 1; j < end; j++)
                preview.Add(new PreviewLine(j + 1, Lines[j]));

            Blocks.Add(new Block
            {
                Bid = "B" + (k + 1),
                Num = h.Num,
                Title = string.IsNullOrWhiteSpace(h.Title) ? "(제목 없음)" : h.Title,
                Anchor = Keyword(h.Title),
                From = h.Line,
                To = end,
                Indent = h.Indent,
                Models = models,
                Preview = preview
            });
        }

        // 직계 자식만 (범위 안 + 더 깊은 들여쓰기 중 최상위 레벨)
        foreach (var b in Blocks)
        {
            var inside = Blocks.Where(c => c != b && c.From > b.From && c.To <= b.To && c.Indent > b.Indent).ToList();
            if (inside.Count == 0) continue;
            int min = inside.Min(c => c.Indent);
            b.Children.AddRange(inside.Where(c => c.Indent == min).Select(c => c.Bid));
        }

        // 영문 블록 파싱 + 1:1 짝짓기
        BuildEnglishBlocks();

        return IndexCheck();
    }

    static string Keyword(string title)
    {
        var w = Regex.Replace(title ?? "", @"[:：].*$", "").Trim();
        if (w.Length == 0) return "(무제)";
        var parts = w.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Take(2));
    }

    /// <summary>앵커 키워드로 블록을 다시 찾는다. 번호는 보지 않는다.</summary>
    public Block? FindByAnchor(string anchor) =>
        Blocks.FirstOrDefault(b => b.Anchor == anchor)
        ?? Blocks.FirstOrDefault(b => b.Title.Contains(anchor, StringComparison.OrdinalIgnoreCase));

    // ── 영문 블록 (한글 블록과 1:1 대응) ─────────────────

    /// <summary>영문 구간의 블록들. Analyze() 이후에 채워진다.</summary>
    public List<Block> EnglishBlocks { get; private set; } = new();

    /// <summary>
    /// 한글 블록 Bid → 대응하는 영문 블록.
    ///
    /// 한↔영은 1:1 대응이므로 "같은 번호 + 같은 등장 순서"로 짝을 짓는다.
    /// 제목은 번역되어 다르므로(제품버전 vs Product version) 제목으로는 못 찾는다.
    /// </summary>
    public Dictionary<string, Block> Pairs { get; private set; } = new();

    /// <summary>
    /// 영문 구간을 파싱해 EnglishBlocks 를 만들고 한글 블록과 짝짓는다.
    /// 규칙을 한글 블록에만 걸어도 영문에 자동으로 같이 적용하기 위한 것.
    /// </summary>
    void BuildEnglishBlocks()
    {
        EnglishBlocks = new List<Block>();
        Pairs = new Dictionary<string, Block>();

        if (!UseEnglish || EnglishAt < 0) return;

        var heads = new List<(string Num, string Title, int Line, int Indent)>();
        for (int i = EnglishAt; i < Lines.Length; i++)
        {
            var m = RxHead.Match(Lines[i]);
            if (!m.Success) continue;
            int indent = Lines[i].TakeWhile(char.IsWhiteSpace).Sum(c => c == '\t' ? 4 : 1);
            heads.Add((m.Groups[1].Value, m.Groups[2].Value.Trim(), i, indent));
        }

        for (int k = 0; k < heads.Count; k++)
        {
            var h = heads[k];
            int end = Lines.Length;
            for (int j = k + 1; j < heads.Count; j++)
                if (heads[j].Indent <= h.Indent) { end = heads[j].Line; break; }

            var models = new List<string>();
            for (int j = h.Line; j < end; j++)
                foreach (var mm in MatchModels(Lines[j]))
                    if (!models.Contains(mm)) models.Add(mm);

            EnglishBlocks.Add(new Block
            {
                Bid = "E" + (k + 1),
                Num = h.Num,
                Title = string.IsNullOrWhiteSpace(h.Title) ? "(제목 없음)" : h.Title,
                Anchor = Keyword(h.Title),
                From = h.Line,
                To = end,
                Indent = h.Indent,
                Models = models
            });
        }

        // 짝짓기: 번호가 같은 것끼리, 같은 번호가 여러 번이면 등장 순서대로
        var used = new HashSet<string>();
        foreach (var ko in Blocks)
        {
            var cand = EnglishBlocks.FirstOrDefault(
                en => en.Num == ko.Num && !used.Contains(en.Bid));
            if (cand is null) continue;
            used.Add(cand.Bid);
            Pairs[ko.Bid] = cand;
        }
    }

    // ── 인덱스 검증 ────────────────────────────────────

    /// <summary>
    /// 번호 라인을 "런" 단위로 쪼갠다. 런은 다음에서 끊긴다:
    ///   1) 더 얕은 들여쓰기의 번호가 나옴 (부모가 바뀜)
    ///   2) 같은 들여쓰기인데 1로 되감김 (새 목록 시작)
    /// 덕분에 "6-1) 아래 1)2)3)" 다음에 오는 또 다른 "1)2)3)" 이
    /// 중복/역전이 아니라 별개의 정상 목록으로 취급된다.
    /// </summary>
    public List<NumberRun> BuildRuns()
    {
        var runs = new List<NumberRun>();
        var open = new List<NumberRun>();

        for (int i = 0; i < Lines.Length; i++)
        {
            var m = RxNumbered.Match(Lines[i]);
            if (!m.Success) continue;

            string zone = (UseEnglish && EnglishAt >= 0 && i >= EnglishAt) ? "EN" : "KO";
            int indent = m.Groups[1].Value.Sum(c => c == '\t' ? 4 : 1);
            string raw = m.Groups[2].Value;
            string prefix = raw.Contains('-') ? raw.Split('-')[0] + "-" : "";
            int val = int.Parse(raw.Split('-').Last(), CultureInfo.InvariantCulture);

            while (open.Count > 0 && open[^1].Indent > indent) open.RemoveAt(open.Count - 1);

            var cur = open.Count > 0 ? open[^1] : null;
            bool sameSlot = cur is not null && cur.Indent == indent && cur.Zone == zone && cur.Prefix == prefix;
            bool rewind = sameSlot && val <= cur!.Items[^1].Val && val <= 1;

            if (!sameSlot || rewind)
            {
                if (sameSlot) open.RemoveAt(open.Count - 1);
                cur = new NumberRun { Zone = zone, Indent = indent, Prefix = prefix };
                runs.Add(cur);
                open.Add(cur);
            }
            cur!.Items.Add((val, i));
        }
        return runs;
    }

    public List<Issue> IndexCheck()
    {
        var issues = new List<Issue>();

        foreach (var run in BuildRuns())
        {
            if (run.Items.Count < 2) continue;

            var seen = new HashSet<int>();
            int? prev = null;

            foreach (var (val, line) in run.Items)
            {
                if (!seen.Add(val))
                    issues.Add(new Issue { Sev = Severity.Error, Line = line,
                        Msg = $"번호 {val} 중복 · {run.Label} ({run.Zone})" });

                if (prev is int p)
                {
                    if (val < p)
                        issues.Add(new Issue { Sev = Severity.Error, Line = line,
                            Msg = $"번호 순서 역전: {p} → {val} · {run.Label} ({run.Zone})" });
                    else if (val > p + 1)
                        for (int k = p + 1; k < val; k++)
                            issues.Add(new Issue { Sev = Severity.Error, Line = line,
                                Msg = $"번호 {k} 누락 · {run.Label} ({run.Zone})" });
                }
                prev = val;
            }

            int first = run.Items[0].Val;
            if (first != 0 && first != 1)
                issues.Add(new Issue { Sev = Severity.Warning, Line = run.Items[0].Line,
                    Msg = $"시작 번호가 {first} · {run.Label} (0 또는 1 권장)" });
        }

        issues.AddRange(IndentCheck());
        return issues;
    }

    // ── 들여쓰기 검증 ──────────────────────────────────

    /// <summary>
    /// 들여쓰기는 공백만 쓴다. 탭이 섞이면 편집기 설정에 따라 정렬이 달라지므로 오류.
    /// 같은 접두어(4-n, 6-n 등)를 쓰는 형제 항목은 들여쓰기 폭도 같아야 한다.
    /// </summary>
    public List<Issue> IndentCheck()
    {
        var issues = new List<Issue>();

        // 1) 탭 문자 검출
        for (int i = 0; i < Lines.Length; i++)
        {
            string L = Lines[i];
            if (L.Length == 0) continue;

            int lead = L.TakeWhile(c => c == ' ' || c == '\t').Count();
            string prefix = L[..lead];

            if (prefix.Contains('\t'))
                issues.Add(new Issue { Sev = Severity.Error, Line = i,
                    Msg = "들여쓰기에 탭 문자 사용 — 공백으로 바꾸세요" });
            else if (L[lead..].Contains('\t'))
                issues.Add(new Issue { Sev = Severity.Warning, Line = i,
                    Msg = "줄 안에 탭 문자 포함 — 정렬이 편집기마다 달라집니다" });
        }

        // 2) 같은 접두어를 쓰는 형제 항목의 들여쓰기 폭 통일
        //    (4-1, 4-2, 4-3 은 모두 같은 칸에서 시작해야 한다)
        var byPrefix = new Dictionary<string, List<(int Line, int Width)>>();
        for (int i = 0; i < Lines.Length; i++)
        {
            var m = RxNumbered.Match(Lines[i]);
            if (!m.Success) continue;

            string raw = m.Groups[2].Value;
            if (!raw.Contains('-')) continue;          // 절차 번호는 문맥마다 달라 제외

            string zone = (UseEnglish && EnglishAt >= 0 && i >= EnglishAt) ? "EN" : "KO";
            string key = $"{zone}|{raw.Split('-')[0]}-";
            int width = m.Groups[1].Value.Sum(c => c == '\t' ? 4 : 1);

            if (!byPrefix.TryGetValue(key, out var list))
                byPrefix[key] = list = new List<(int, int)>();
            list.Add((i, width));
        }

        foreach (var (key, items) in byPrefix)
        {
            if (items.Count < 2) continue;

            // 최빈 폭을 기준으로 삼는다
            int modal = items.GroupBy(x => x.Width)
                             .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                             .First().Key;

            foreach (var (line, w) in items.Where(x => x.Width != modal))
                issues.Add(new Issue { Sev = Severity.Warning, Line = line,
                    Msg = $"들여쓰기 폭 불일치 · {key.Split('|')[1]}n (기준 {modal}칸 ≠ {w}칸)" });
        }

        return issues;
    }

    // ── 한↔영 페어링 ───────────────────────────────────

    public sealed record EnBlock(int From, int To, List<string> Models);

    public EnBlock? FindEnglishBlock(string num)
    {
        if (EnglishAt < 0) return null;

        for (int i = EnglishAt; i < Lines.Length; i++)
        {
            var m = RxHead.Match(Lines[i]);
            if (!m.Success || m.Groups[1].Value != num) continue;

            int indent = Lines[i].TakeWhile(char.IsWhiteSpace).Sum(c => c == '\t' ? 4 : 1);
            int end = Lines.Length;
            for (int j = i + 1; j < Lines.Length; j++)
            {
                var n = RxNumbered.Match(Lines[j]);
                if (n.Success && n.Groups[1].Value.Sum(c => c == '\t' ? 4 : 1) <= indent) { end = j; break; }
            }

            var models = new List<string>();
            for (int j = i; j < end; j++)
                foreach (var mm in MatchModels(Lines[j]))
                    if (!models.Contains(mm)) models.Add(mm);

            return new EnBlock(i, end, models);
        }
        return null;
    }

    public IEnumerable<string> PairReport()
    {
        if (!UseEnglish || EnglishAt < 0) { yield return "영문 경계를 지정하세요."; yield break; }

        var ko = new List<(string Id, int Line)>();
        var en = new List<(string Id, int Line)>();
        for (int i = 0; i < Lines.Length; i++)
        {
            var m = RxHead.Match(Lines[i]);
            if (!m.Success) continue;
            (i < EnglishAt ? ko : en).Add((m.Groups[1].Value, i));
        }

        var ids = ko.Select(x => x.Id).Distinct().ToList();
        foreach (var id in ids)
        {
            var a = ko.First(x => x.Id == id);
            var b = en.FirstOrDefault(x => x.Id == id);
            yield return b.Id is null
                ? $"✗ {id} · L{a.Line + 1} ↔ 짝 없음"
                : $"✓ {id} · L{a.Line + 1} ↔ L{b.Line + 1}";
        }
        foreach (var o in en.Where(x => !ids.Contains(x.Id)))
            yield return $"! {o.Id} · 영문에만 존재";
    }

    /// <summary>
    /// 짝이 없는 블록은 실제 오류다. 페어링 패널에만 보여주고 끝내지 않고
    /// 검증 결과에도 올린다.
    /// </summary>
    public List<Issue> PairIssues()
    {
        var issues = new List<Issue>();
        if (!UseEnglish || EnglishAt < 0) return issues;

        var ko = new List<(string Id, int Line)>();
        var en = new List<(string Id, int Line)>();
        for (int i = 0; i < Lines.Length; i++)
        {
            var m = RxHead.Match(Lines[i]);
            if (!m.Success) continue;
            (i < EnglishAt ? ko : en).Add((m.Groups[1].Value, i));
        }

        var koIds = ko.Select(x => x.Id).Distinct().ToList();
        var enIds = en.Select(x => x.Id).Distinct().ToList();

        foreach (var id in koIds.Where(x => !enIds.Contains(x)))
            issues.Add(new Issue { Sev = Severity.Error, Line = ko.First(x => x.Id == id).Line,
                Msg = $"블록 {id} 이 한국어에만 존재 — 영문 짝 없음" });

        foreach (var id in enIds.Where(x => !koIds.Contains(x)))
            issues.Add(new Issue { Sev = Severity.Error, Line = en.First(x => x.Id == id).Line,
                Msg = $"블록 {id} 이 영문에만 존재 — 한국어 짝 없음" });

        return issues;
    }
}
