using SibangGenerator.Models;

namespace SibangGenerator.Services;

/// <summary>
/// ② 단계의 버전 그룹을 실제 데이터로 채우고,
/// 4-N 블록 단위로 다시 묶는다.
/// </summary>
public static class ModelResolver
{
    /// <summary>
    /// 버전 그룹 → 모델 하나하나로 펼치고 BOM · 폴더 · 파일목록을 채운다.
    /// Order 는 ② 단계 입력 순서. 4-N 정렬에 그대로 쓴다.
    ///
    /// 엑셀이나 펌웨어 폴더가 없어도 동작한다.
    /// 없으면 해당 항목만 비워둔 채 나머지를 채운다 — 그래야 사용자가
    /// 조회 결과를 보고 BOM 을 직접 지정할 수 있다.
    ///
    /// VersionGroup.BomOverrides 에 값이 있으면 엑셀보다 우선한다.
    /// </summary>
    public static List<ResolvedModel> Resolve(
        IEnumerable<VersionGroup> groups,
        BomLookup? bom,
        FirmwareScanner? fw)
    {
        var result = new List<ResolvedModel>();
        int order = 0;

        foreach (var g in groups)
            foreach (var name in g.Models)
            {
                var m = new ResolvedModel
                {
                    Name = name,
                    Version = g.Version,
                    Order = order++
                };

                // 1) 엑셀 조회 (없으면 null)
                m.ExcelBom = (bom is not null && bom.Ready) ? bom.Find(name) : null;

                // 2) 사용자 지정 BOM 이 있으면 덮어쓴다
                if (g.BomOverrides.TryGetValue(name, out var manual) &&
                    !string.IsNullOrWhiteSpace(manual))
                    m.ManualBom = manual;

                // 3) 최종 BOM 으로 폴더 확인
                //    Files 를 먼저 채운 뒤 FolderFound 를 세팅한다.
                //    FolderFound setter 가 Status 변경을 알리므로 순서가 중요하다.
                if (m.Bom is not null && fw is not null && fw.Ready)
                {
                    bool exists = fw.FolderExists(m.Bom, m.Version);
                    if (exists) m.Files = fw.ReadFiles(m.Bom, m.Version);
                    m.FolderFound = exists;
                }
                result.Add(m);
            }

        return result;
    }

    /// <summary>
    /// (BOM, Ver) 쌍으로 묶어 4-N 블록을 만든다.
    ///
    /// - 같은 BOM + 같은 버전  → 한 블록에 모델 나열
    /// - 같은 BOM + 다른 버전  → 별개 블록
    ///
    /// 블록 순서는 각 그룹에서 가장 먼저 등장한 모델의 Order 를 따른다.
    /// 즉 ② 단계 입력 순서가 그대로 보존된다.
    /// </summary>
    public static List<ProductVersionBlock> BuildBlocks(
        IEnumerable<ResolvedModel> models,
        WorkspaceSettings ws)
    {
        return models
            .Where(m => m.BomFound && m.FolderFound)
            .GroupBy(m => (m.Bom!, m.Version))
            .OrderBy(g => g.Min(m => m.Order))            // ← 입력 순서 보존
            .Select(g => new ProductVersionBlock
            {
                Bom = g.Key.Item1,
                Version = g.Key.Item2,
                Models = g.OrderBy(m => m.Order).Select(m => m.Name).ToList(),
                Files = g.First().Files,                   // 같은 폴더이므로 아무거나
                Cpu = ws.Cpu
            })
            .ToList();
    }

    /// <summary>등록된 모든 모델명. 필터 판정에 쓴다.</summary>
    public static HashSet<string> AllModelNames(IEnumerable<VersionGroup> groups) =>
        new(groups.SelectMany(g => g.Models), StringComparer.Ordinal);

    /// <summary>텍스트박스에 복붙한 모델명을 파싱한다. 순서 유지, 중복 제거.</summary>
    public static List<string> ParseModels(string raw)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();

        foreach (var t in raw.Split(new[] { ',', '\n', '\r', '\t', ';' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (seen.Add(t)) list.Add(t);

        return list;
    }
}
