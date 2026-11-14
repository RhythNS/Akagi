using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace Akagi.Web.Pages.Projects;

/// <summary>
/// Renders ticket and project Markdown. Raw HTML is shown as text and links only go to web, mail or relative addresses,
/// since text written by models ends up here.
/// </summary>
public static class Markdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static MarkupString ToHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new MarkupString(string.Empty);
        }
        MarkdownDocument document = Markdig.Markdown.Parse(text, Pipeline);
        foreach (LinkInline link in document.Descendants<LinkInline>())
        {
            if (!IsSafe(link.Url))
            {
                link.Url = "#";
            }
        }
        return new MarkupString(document.ToHtml(Pipeline));
    }

    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }
        string trimmed = url.Trim();
        int colon = trimmed.IndexOf(':');
        int slash = trimmed.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (slash >= 0 && slash < colon))
        {
            // No scheme: a relative address
            return true;
        }
        string scheme = trimmed[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto";
    }
}
