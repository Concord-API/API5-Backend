using Ratio.Application.Abstractions;

namespace Ratio.Application.DataSources;

public static class ProvenanceCatalog
{
    private static readonly Dictionary<string, (string Name, string Url)> Known = new()
    {
        ["datajud"] = ("DataJud/CNJ", "https://www.cnj.jus.br/sistemas/datajud/"),
        ["doaj"] = ("DOAJ", "https://doaj.org/"),
        ["scielo"] = ("SciELO", "https://www.scielo.br/"),
        ["oai_indexlaw"] = ("IndexLaw", "https://www.indexlaw.org/"),
        ["oai_emerj"] = ("Revista da EMERJ", "https://ojs.emerj.com.br/index.php/revistadaemerj"),
        ["oai_ejef_tjmg"] = ("Revista da EJEF (TJMG)", "https://revistaejef.tjmg.jus.br/index.php/revista-ejef")
    };

    public static Provenance Describe(LoadedProvenance loaded) =>
        new(loaded.Sources.Where(IsComplete).Select(Describe).ToArray(), loaded.MethodologyVersion);

    private static bool IsComplete(LoadedSource loaded) =>
        !string.IsNullOrWhiteSpace(loaded.Block)
        && !string.IsNullOrWhiteSpace(loaded.Source)
        && loaded.ExtractedAt is not null;

    private static ProvenanceSource Describe(LoadedSource loaded)
    {
        var code = loaded.Source!;
        var (name, url) = Known.TryGetValue(code, out var known) ? known : (code, null);
        return new ProvenanceSource(loaded.Block!, code, name, url, loaded.ExtractedAt!.Value, loaded.Count);
    }
}
