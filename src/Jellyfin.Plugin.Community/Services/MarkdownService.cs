using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class MarkdownService
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UsePipeTables()
            .UseTaskLists()
            .UseListExtras()
            .Build();
    }

    public string Render(string markdown)
    {
        var normalized = Normalize(markdown);
        var html = Markdown.ToHtml(normalized, _pipeline);
        html = DangerousProtocolRegex().Replace(html, "href=\"#\"");
        html = RemoteImageRegex().Replace(html, match =>
        {
            var allowRemote = Plugin.Instance?.Configuration.AllowRemoteImages == true;
            return allowRemote ? match.Value : WebUtility.HtmlEncode(match.Groups[1].Value);
        });
        return html;
    }

    public string Normalize(string markdown)
    {
        var value = (markdown ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return value.Trim();
    }

    public IReadOnlyList<string> ExtractMentions(string markdown)
    {
        return MentionRegex().Matches(markdown ?? string.Empty)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, Plugin.Instance?.Configuration.MaxMentionsPerPost ?? 10))
            .ToArray();
    }

    public void Validate(string title, string body)
    {
        var configuration = Plugin.Instance?.Configuration;
        var maxTitle = Math.Max(1, configuration?.MaxTitleLength ?? 200);
        var maxBody = Math.Max(1, configuration?.MaxPostLength ?? 20_000);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > maxTitle)
        {
            throw new CommunityValidationException($"Title must contain between 1 and {maxTitle} characters.");
        }

        ValidateBody(body, maxBody);
    }

    public void ValidateBody(string body)
    {
        ValidateBody(body, Math.Max(1, Plugin.Instance?.Configuration.MaxPostLength ?? 20_000));
    }

    private static void ValidateBody(string body, int maxBody)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > maxBody)
        {
            throw new CommunityValidationException($"Post body must contain between 1 and {maxBody} characters.");
        }

        var maxMentions = Math.Max(0, Plugin.Instance?.Configuration.MaxMentionsPerPost ?? 10);
        var mentionCount = MentionRegex().Matches(body).Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (mentionCount > maxMentions)
        {
            throw new CommunityValidationException($"A post cannot mention more than {maxMentions} users.");
        }

        var blocked = Plugin.Instance?.Configuration.BlockedTerms ?? [];
        var match = blocked.FirstOrDefault(term =>
            !string.IsNullOrWhiteSpace(term)
            && body.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            throw new CommunityValidationException("The post contains a term blocked by the server administrator.");
        }
    }

    [GeneratedRegex("(?<![\\w@])@([\\p{L}\\p{N}_.-]{2,64})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    [GeneratedRegex("href=\"(?:javascript|data|vbscript):[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousProtocolRegex();

    [GeneratedRegex("<img[^>]+alt=\"([^\"]*)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoteImageRegex();
}
