namespace Akagi.Graphs;

/// <summary>
/// Command line front end for graph tooling. Hosted by the CharacterEditor in headless mode.
/// Exit codes: 0 success, 1 validation or composition failed, 2 usage error.
/// </summary>
internal class GraphCli
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;

    private readonly TextWriter _out;
    private readonly TextWriter _error;

    public GraphCli(TextWriter output, TextWriter error)
    {
        _out = output;
        _error = error;
    }

    public static bool IsCliInvocation(string[] args)
    {
        return args.Length > 0 && args[0].StartsWith('-');
    }

    public static string Usage =>
        """
        Akagi CharacterEditor headless mode

        Usage:
          --validate <graph.json|directory>... [--json] [--search <dir>] [--strict]
              Checks graph files for unknown node types, invalid property values, dangling or mistyped
              connections, unconnected references and unresolvable graph references.
              Directories validate every *.json inside. --strict also fails on warnings.
          --dump-nodes [output.json] [--text]
              Writes the catalog of all [GraphNode] types (properties, inputs, output types) as JSON,
              or as a compact text listing with --text. Prints to stdout when no file is given.
          --describe-node <TypeName>
              Prints the catalog entry of a single node type.
          --compose <graph.json> [-o output.json] [--search <dir>]
              Inlines all graph references and writes the flattened graph.
          --bundle <graph.json> [-o output.json] [--search <dir>]
              Writes the graph with every referenced subgraph embedded, as one file to upload with
              /uploadGraph. Without -o it writes <name>.bundle.json next to the graph.
          --help
              Shows this text.
        """;

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            _error.WriteLine(Usage);
            return ExitUsage;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "--validate" => await Validate(args[1..]),
                "--dump-nodes" => DumpNodes(args[1..]),
                "--describe-node" => DescribeNode(args[1..]),
                "--compose" => await Compose(args[1..]),
                "--bundle" => await Bundle(args[1..]),
                "--help" or "-h" or "/?" => Help(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            _error.WriteLine($"error: {ex.Message}");
            return ExitFailed;
        }
    }

    private int Help()
    {
        _out.WriteLine(Usage);
        return ExitOk;
    }

    private int UnknownCommand(string command)
    {
        _error.WriteLine($"Unknown command '{command}'.");
        _error.WriteLine(Usage);
        return ExitUsage;
    }

    private async Task<int> Validate(string[] args)
    {
        bool json = false;
        bool strict = false;
        List<string> searchDirectories = [];
        List<string> targets = [];

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--json":
                    json = true;
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--search":
                    if (++i >= args.Length)
                    {
                        _error.WriteLine("--search needs a directory.");
                        return ExitUsage;
                    }
                    searchDirectories.Add(args[i]);
                    break;
                default:
                    targets.Add(args[i]);
                    break;
            }
        }

        if (targets.Count == 0)
        {
            _error.WriteLine("--validate needs at least one graph file or directory.");
            return ExitUsage;
        }

        List<string> files = [];
        foreach (string target in targets)
        {
            if (Directory.Exists(target))
            {
                files.AddRange(Directory.EnumerateFiles(target, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                files.Add(target);
            }
        }

        GraphValidator validator = new(new FileGraphResolver([.. searchDirectories]));
        List<GraphValidationReport> reports = [];
        foreach (string file in files)
        {
            reports.Add(await validator.ValidateFileAsync(file));
        }
        GraphValidator.CheckDuplicateGraphIds(reports);

        if (json)
        {
            _out.WriteLine(reports.Count == 1
                ? reports[0].ToJson()
                : "[" + string.Join("," + Environment.NewLine, reports.Select(r => r.ToJson())) + "]");
        }
        else
        {
            foreach (GraphValidationReport report in reports)
            {
                _out.WriteLine(report.ToText());
            }
        }

        bool failed = reports.Any(r => !r.IsValid || (strict && r.WarningCount > 0));
        return failed ? ExitFailed : ExitOk;
    }

    private int DumpNodes(string[] args)
    {
        bool text = args.Any(a => string.Equals(a, "--text", StringComparison.OrdinalIgnoreCase));
        string? outputPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        NodeCatalog catalog = NodeCatalog.Build();
        string content = text ? catalog.ToText() : catalog.ToJson();

        if (outputPath == null)
        {
            _out.WriteLine(content);
        }
        else
        {
            File.WriteAllText(outputPath, content);
            _out.WriteLine($"Wrote {catalog.Nodes.Count} node types to {Path.GetFullPath(outputPath)}");
        }
        return ExitOk;
    }

    private int DescribeNode(string[] args)
    {
        if (args.Length == 0)
        {
            _error.WriteLine("--describe-node needs a type name.");
            return ExitUsage;
        }

        NodeCatalog catalog = NodeCatalog.Build();
        NodeCatalog.NodeTypeInfo? node = catalog.Find(args[0]);
        if (node == null)
        {
            _error.WriteLine($"No node type named '{args[0]}'. Known types: {string.Join(", ", catalog.Nodes.Select(n => n.Name))}");
            return ExitFailed;
        }

        _out.WriteLine(System.Text.Json.JsonSerializer.Serialize(node, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return ExitOk;
    }

    private async Task<int> Bundle(string[] args)
    {
        if (!TryParseInputOutput(args, "--bundle", out string? input, out string? output, out List<string> searchDirectories, out int exitCode))
        {
            return exitCode;
        }

        string fullPath = Path.GetFullPath(input!);
        GraphBundler.BundleResult result = await GraphBundler.BundleAsync(GraphData.Load(fullPath),
            new FileGraphResolver([.. searchDirectories]), Path.GetDirectoryName(fullPath));
        foreach (string error in result.Errors)
        {
            _error.WriteLine($"error: {error}");
        }
        if (!result.Success)
        {
            return ExitFailed;
        }

        output ??= BundleFileName(fullPath);
        File.WriteAllText(output, result.Bundle.ToJson());
        _out.WriteLine($"Wrote bundle with {result.Bundle.Embedded?.Count ?? 0} embedded graph(s) to {Path.GetFullPath(output)}");
        foreach (EmbeddedGraph embedded in result.Bundle.Embedded ?? [])
        {
            _out.WriteLine($"  {embedded.Path} ({embedded.Graph.GraphId})");
        }
        return ExitOk;
    }

    /// <summary>
    /// "Companion.json" becomes "Companion.bundle.json" in the same directory.
    /// </summary>
    public static string BundleFileName(string graphPath) =>
        Path.Combine(Path.GetDirectoryName(graphPath) ?? string.Empty, Path.GetFileNameWithoutExtension(graphPath) + ".bundle.json");

    private bool TryParseInputOutput(string[] args, string command, out string? input, out string? output, out List<string> searchDirectories, out int exitCode)
    {
        input = null;
        output = null;
        searchDirectories = [];
        exitCode = ExitOk;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-o":
                case "--output":
                    if (++i >= args.Length)
                    {
                        _error.WriteLine("-o needs a file name.");
                        exitCode = ExitUsage;
                        return false;
                    }
                    output = args[i];
                    break;
                case "--search":
                    if (++i >= args.Length)
                    {
                        _error.WriteLine("--search needs a directory.");
                        exitCode = ExitUsage;
                        return false;
                    }
                    searchDirectories.Add(args[i]);
                    break;
                default:
                    input = args[i];
                    break;
            }
        }
        if (input == null)
        {
            _error.WriteLine($"{command} needs a graph file.");
            exitCode = ExitUsage;
            return false;
        }
        return true;
    }

    private async Task<int> Compose(string[] args)
    {
        if (!TryParseInputOutput(args, "--compose", out string? input, out string? output, out List<string> searchDirectories, out int exitCode))
        {
            return exitCode;
        }

        string fullPath = Path.GetFullPath(input!);
        GraphData graph = GraphData.Load(fullPath);
        GraphComposer composer = new(new FileGraphResolver([.. searchDirectories]));
        GraphCompositionResult result = await composer.ComposeAsync(graph, Path.GetDirectoryName(fullPath), fullPath);

        foreach (string error in result.Errors)
        {
            _error.WriteLine($"error: {error}");
        }
        if (!result.Success)
        {
            return ExitFailed;
        }

        string json = result.Graph.ToJson();
        if (output == null)
        {
            _out.WriteLine(json);
        }
        else
        {
            File.WriteAllText(output, json);
            _out.WriteLine($"Wrote composed graph with {result.Graph.Nodes.Count} nodes to {Path.GetFullPath(output)}");
        }
        return ExitOk;
    }
}
