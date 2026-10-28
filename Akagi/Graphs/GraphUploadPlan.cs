namespace Akagi.Graphs;

/// <summary>
/// Decides what an upload of several graph files does, independent of the order the files arrive in:
/// every graph (and every graph embedded in a bundle) is stored as a definition so references resolve, and only the
/// root graphs, the ones no other uploaded graph references, are instantiated.
/// </summary>
internal class GraphUploadPlan
{
    public record UploadedGraph(string FileName, GraphData Graph);

    public record Definition(string GraphId, string FileName, string Json);

    public List<Definition> Definitions { get; } = [];
    public List<UploadedGraph> Roots { get; } = [];
    /// <summary>
    /// Uploaded files that are only used as subgraphs of another upload, with the file that references them.
    /// </summary>
    public List<(UploadedGraph Graph, string UsedBy)> Subgraphs { get; } = [];

    public static GraphUploadPlan Create(IReadOnlyList<UploadedGraph> uploads, bool instantiateAll = false)
    {
        GraphUploadPlan plan = new();

        foreach (UploadedGraph upload in uploads)
        {
            foreach (EmbeddedGraph embedded in upload.Graph.Embedded ?? [])
            {
                plan.Definitions.Add(new Definition(embedded.Graph.GraphId, Path.GetFileName(embedded.Path), embedded.Graph.ToJson()));
            }
            // The root of a bundle is stored without its embedded copies; they are stored on their own above
            GraphData stored = upload.Graph.IsBundle ? WithoutEmbedded(upload.Graph) : upload.Graph;
            plan.Definitions.Add(new Definition(upload.Graph.GraphId, upload.FileName, stored.ToJson()));
        }

        foreach (UploadedGraph upload in uploads)
        {
            string? usedBy = instantiateAll ? null : FindUser(upload, uploads);
            if (usedBy == null)
            {
                plan.Roots.Add(upload);
            }
            else
            {
                plan.Subgraphs.Add((upload, usedBy));
            }
        }
        return plan;
    }

    /// <summary>
    /// The first other upload that references this graph directly, through one of its embedded graphs,
    /// or ships it as an embedded graph.
    /// </summary>
    private static string? FindUser(UploadedGraph candidate, IReadOnlyList<UploadedGraph> uploads)
    {
        foreach (UploadedGraph other in uploads.Where(u => !ReferenceEquals(u, candidate)))
        {
            if (other.Graph.Embedded?.Any(e => string.Equals(e.Graph.GraphId, candidate.Graph.GraphId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return other.FileName;
            }

            IEnumerable<GraphData> graphs = [other.Graph, .. (other.Graph.Embedded ?? []).Select(e => e.Graph)];
            if (graphs.SelectMany(g => GraphBundler.References(g)).Any(r => Targets(r.Reference, candidate)))
            {
                return other.FileName;
            }
        }
        return null;
    }

    private static bool Targets(GraphReference reference, UploadedGraph candidate) =>
        (!string.IsNullOrWhiteSpace(reference.GraphId) && string.Equals(reference.GraphId, candidate.Graph.GraphId, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(reference.GraphPath)
            && string.Equals(Path.GetFileName(EmbeddedGraphResolver.NormalizePath(reference.GraphPath)), candidate.FileName, StringComparison.OrdinalIgnoreCase));

    private static GraphData WithoutEmbedded(GraphData graph)
    {
        GraphData copy = graph.Clone();
        copy.Embedded = null;
        return copy;
    }
}
