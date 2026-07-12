using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SibangGenerator.Models;

namespace SibangGenerator.Services;

public static class GeneratorStore
{
    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글이 \uXXXX 로 깨지지 않게
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(string path, GeneratorSet set) =>
        File.WriteAllText(path, JsonSerializer.Serialize(set, Opt), new UTF8Encoding(true));

    public static GeneratorSet Load(string path) =>
        JsonSerializer.Deserialize<GeneratorSet>(File.ReadAllText(path), Opt)
        ?? throw new InvalidDataException("규칙 세트를 읽을 수 없습니다.");

    /// <summary>
    /// 저장된 규칙을 현재 문서에 다시 붙인다.
    /// 번호(6-1, 4-2 등)는 무시하고 앵커 키워드로만 찾는다.
    /// 그래서 6-1 이 사라지고 6-2 가 6-1 이 되어도 그대로 연결된다.
    /// </summary>
    public static (List<TransformRule> Linked, int Missing) Reconnect(
        GeneratorSet set, SpecParser parser)
    {
        var linked = new List<TransformRule>();
        int missing = 0, no = 1;

        foreach (var r in set.Rules)
        {
            var b = parser.FindByAnchor(r.AnchorKeyword);
            if (b is null) { missing++; continue; }

            r.No = no++;
            r.Bid = b.Bid;
            r.Num = b.Num;
            r.Title = b.Title;
            r.AnchorKeyword = b.Anchor;
            linked.Add(r);
        }
        return (linked, missing);
    }
}

public static class SpecWriter
{
    /// <summary>생성된 시방을 저장한다. 원본과 같은 UTF-8 BOM.</summary>
    public static void Write(string path, IEnumerable<string> lines) =>
        File.WriteAllText(path, string.Join("\r\n", lines), new UTF8Encoding(true));

    /// <summary>생성 로그를 텍스트로.</summary>
    public static string BuildLog(GenerationResult r, IReadOnlyList<VersionGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("시방 생성 로그");
        sb.AppendLine($"생성  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("등록 모델");
        foreach (var g in groups)
            sb.AppendLine($"  [{g.Version}]  {string.Join(", ", g.Models)}");
        sb.AppendLine();

        sb.AppendLine($"요약  오류 {r.ErrorCount} · 경고 {r.WarnCount} · 총 {r.Lines.Length}줄");
        sb.AppendLine();

        foreach (var sev in new[] { Severity.Error, Severity.Warning, Severity.Info })
        {
            var rows = r.Log.Where(x => x.Sev == sev).ToList();
            if (rows.Count == 0) continue;

            string label = sev switch
            {
                Severity.Error => "오류",
                Severity.Warning => "경고",
                _ => "정보"
            };
            sb.AppendLine($"[{label}] {rows.Count}건");
            sb.AppendLine(new string('─', 60));
            foreach (var x in rows)
                sb.AppendLine($"  L{x.Line + 1,-6} {x.Msg}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
