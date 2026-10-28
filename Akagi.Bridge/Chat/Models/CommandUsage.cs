using System.Text;
using System.Text.RegularExpressions;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// Reads the usage line of a command description into structured parameters, so a client can show a form instead
/// of asking for "/command arg arg". The grammar is the one the descriptions already use:
/// <list type="bullet">
/// <item>&lt;name&gt; a required value, [name] an optional one, a "..." suffix takes the rest of the line</item>
/// <item>a|b|c a choice, [a|b] an optional one; an option may be followed by its own parameters: [end|summary &lt;text&gt;]</item>
/// <item>[key=default] a named value sent as key=value; [--flag] and (word) optional words</item>
/// <item>bare words are fixed words; " | " separates alternative forms: "/fitbit link | /fitbit code &lt;code&gt;"</item>
/// </list>
/// </summary>
public static partial class CommandUsage
{
    public record UsageText(string Summary, string? Usage);

    [GeneratedRegex(@"\bUsage\b\s*:?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex UsageKeyword();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Splits "Does X. Usage: /x &lt;y&gt;. More text." into the prose and the usage line.
    /// The usage line ends at the first period, outside brackets, that is followed by whitespace.
    /// </summary>
    public static UsageText Split(string description)
    {
        Match match = UsageKeyword().Match(description);
        if (!match.Success)
        {
            return new UsageText(Clean(description), null);
        }

        int start = match.Index + match.Length;
        int end = description.Length;
        int depth = 0;
        for (int i = start; i < description.Length; i++)
        {
            char c = description[i];
            if (c is '<' or '[' or '(')
            {
                depth++;
            }
            else if (c is '>' or ']' or ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == '.' && depth == 0 && (i + 1 == description.Length || char.IsWhiteSpace(description[i + 1])))
            {
                end = i;
                break;
            }
        }

        string usage = description[start..end].Trim();
        string rest = end < description.Length ? description[(end + 1)..] : string.Empty;
        string summary = Clean(description[..match.Index] + " " + rest);
        return new UsageText(summary, usage.Length == 0 ? null : usage);
    }

    private static string Clean(string text)
    {
        string cleaned = Whitespace().Replace(text, " ").Trim();
        // "Adds a goal. " lost its usage line; "Usage /x" without a period leaves nothing to tidy
        return cleaned.EndsWith(" .", StringComparison.Ordinal) ? cleaned[..^2] + "." : cleaned;
    }

    /// <summary>
    /// Parses a usage line into its forms. Leading command names are dropped; files are recognized for commands
    /// that accept them. A command without usage gets one empty form, so it can still be run.
    /// </summary>
    public static CommandForm[] Parse(string commandName, string? usage, bool acceptsFiles = false)
    {
        if (string.IsNullOrWhiteSpace(usage))
        {
            return [new CommandForm { Usage = commandName }];
        }

        List<CommandForm> forms = [];
        foreach (string formText in SplitTopLevel(usage, standalonePipe: true))
        {
            List<string> tokens = Tokenize(formText);
            string bareName = commandName.TrimStart('/');
            if (tokens.Count > 0 && string.Equals(tokens[0].TrimStart('/'), bareName, StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(0);
            }
            string display = tokens.Count == 0 ? commandName : commandName + " " + string.Join(' ', tokens);
            forms.Add(new CommandForm
            {
                Usage = display,
                Parameters = [.. ParseTokens(tokens, acceptsFiles)]
            });
        }
        return [.. forms];
    }

    private static List<CommandParameter> ParseTokens(List<string> tokens, bool acceptsFiles)
    {
        List<CommandParameter> parameters = [];
        foreach (string token in tokens)
        {
            CommandParameter parameter = ParseToken(token, acceptsFiles);
            // A fixed word after a value is prose, as in "<steps> where steps are ..."
            if (parameter.Kind == CommandParameterKind.Literal && !parameter.Optional
                && parameters.Any(p => p.Kind != CommandParameterKind.Literal))
            {
                break;
            }
            parameters.Add(parameter);
        }
        return parameters;
    }

    private static CommandParameter ParseToken(string token, bool acceptsFiles)
    {
        if (token.StartsWith('<') && token.EndsWith('>'))
        {
            return ParseValue(token[1..^1].Trim(), optional: false, acceptsFiles);
        }
        if (token.StartsWith('[') && token.EndsWith(']'))
        {
            string inner = token[1..^1].Trim();
            if (inner.StartsWith('<') && inner.EndsWith('>') && SplitTopLevel(inner[1..^1], standalonePipe: false).Count == 1)
            {
                inner = inner[1..^1].Trim();
            }
            if (inner.StartsWith("--", StringComparison.Ordinal) && !inner.Contains(' '))
            {
                return new CommandParameter { Name = inner, Kind = CommandParameterKind.Literal, Optional = true };
            }
            return ParseValue(inner, optional: true, acceptsFiles);
        }
        if (token.StartsWith('(') && token.EndsWith(')'))
        {
            return new CommandParameter { Name = token[1..^1].Trim(), Kind = CommandParameterKind.Literal, Optional = true };
        }
        if (SplitTopLevel(token, standalonePipe: false).Count > 1)
        {
            return Choice(token, optional: false, acceptsFiles);
        }
        return new CommandParameter { Name = token, Kind = CommandParameterKind.Literal };
    }

    private static CommandParameter ParseValue(string inner, bool optional, bool acceptsFiles)
    {
        if (SplitTopLevel(inner, standalonePipe: false).Count > 1)
        {
            return Choice(inner, optional, acceptsFiles);
        }

        int equals = inner.IndexOf('=');
        if (equals > 0 && !inner[..equals].Contains(' '))
        {
            string key = inner[..equals];
            return new CommandParameter
            {
                Name = key,
                Key = key,
                Kind = CommandParameterKind.Text,
                Optional = optional,
                Default = inner[(equals + 1)..].Trim()
            };
        }

        if (inner.EndsWith("...", StringComparison.Ordinal))
        {
            return new CommandParameter { Name = inner[..^3].Trim(), Kind = CommandParameterKind.Rest, Optional = optional };
        }

        bool isFile = acceptsFiles && inner.Contains("file", StringComparison.OrdinalIgnoreCase);
        return new CommandParameter
        {
            Name = inner,
            Kind = isFile ? CommandParameterKind.File : CommandParameterKind.Text,
            Optional = optional
        };
    }

    private static CommandParameter Choice(string text, bool optional, bool acceptsFiles)
    {
        List<CommandOption> options = [];
        foreach (string optionText in SplitTopLevel(text, standalonePipe: false))
        {
            List<string> tokens = Tokenize(optionText);
            if (tokens.Count == 0)
            {
                continue;
            }
            options.Add(new CommandOption
            {
                Value = tokens[0],
                Parameters = [.. tokens.Skip(1).Select(t => ParseToken(t, acceptsFiles))]
            });
        }
        return new CommandParameter
        {
            Name = string.Join(" / ", options.Select(o => o.Value)),
            Kind = CommandParameterKind.Choice,
            Optional = optional,
            Options = [.. options]
        };
    }

    /// <summary>
    /// Splits at '|' outside brackets. With <paramref name="standalonePipe"/> only a pipe surrounded by spaces
    /// counts, so "a|b" stays one piece.
    /// </summary>
    private static List<string> SplitTopLevel(string text, bool standalonePipe)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '<' or '[' or '(')
            {
                depth++;
            }
            else if (c is '>' or ']' or ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == '|' && depth == 0)
            {
                bool standalone = (i == 0 || char.IsWhiteSpace(text[i - 1])) && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]));
                if (standalone || !standalonePipe)
                {
                    parts.Add(text[start..i].Trim());
                    start = i + 1;
                }
            }
        }
        parts.Add(text[start..].Trim());
        return [.. parts.Where(p => p.Length > 0)];
    }

    /// <summary>
    /// Splits on whitespace outside brackets: "end [reflect|summary &lt;text&gt;]" gives "end" and the bracket.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        int depth = 0;
        foreach (char c in text)
        {
            if (c is '<' or '[' or '(')
            {
                depth++;
            }
            else if (c is '>' or ']' or ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }

    public record BuildResult(string Text, List<string> Missing)
    {
        public bool Complete => Missing.Count == 0;
    }

    /// <summary>
    /// Writes the command line for a filled-in form. <paramref name="valueOf"/> returns the value of a parameter:
    /// the text, the picked option, or anything non-empty to include an optional word. Values with spaces are
    /// wrapped in {{ }} so the server reads them as one argument.
    /// </summary>
    public static BuildResult Build(string commandName, CommandForm form, Func<CommandParameter, string?> valueOf)
    {
        StringBuilder text = new(commandName);
        List<string> missing = [];
        Append(form.Parameters, valueOf, text, missing);
        return new BuildResult(text.ToString(), missing);
    }

    private static void Append(IEnumerable<CommandParameter> parameters, Func<CommandParameter, string?> valueOf, StringBuilder text, List<string> missing)
    {
        foreach (CommandParameter parameter in parameters)
        {
            string? value = valueOf(parameter)?.Trim();
            bool empty = string.IsNullOrEmpty(value);
            switch (parameter.Kind)
            {
                case CommandParameterKind.Literal:
                    if (!parameter.Optional || !empty)
                    {
                        text.Append(' ').Append(parameter.Name);
                    }
                    break;
                case CommandParameterKind.File:
                    break;
                case CommandParameterKind.Choice:
                    CommandOption? option = parameter.Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
                    if (option == null)
                    {
                        if (!parameter.Optional)
                        {
                            missing.Add(parameter.Name);
                        }
                        break;
                    }
                    text.Append(' ').Append(option.Value);
                    Append(option.Parameters, valueOf, text, missing);
                    break;
                case CommandParameterKind.Rest:
                    if (empty)
                    {
                        if (!parameter.Optional)
                        {
                            missing.Add(parameter.Name);
                        }
                        break;
                    }
                    text.Append(' ').Append(value);
                    break;
                default:
                    if (empty)
                    {
                        if (!parameter.Optional)
                        {
                            missing.Add(parameter.Name);
                        }
                        break;
                    }
                    string argument = parameter.Key == null ? value! : parameter.Key + "=" + value;
                    text.Append(' ').Append(Quote(argument));
                    break;
            }
        }
    }

    /// <summary>
    /// "two words" becomes "{{two words}}", the grouping the server's argument parser understands.
    /// </summary>
    public static string Quote(string argument) =>
        argument.Any(char.IsWhiteSpace) || argument.StartsWith("{{", StringComparison.Ordinal) ? "{{" + argument + "}}" : argument;
}
