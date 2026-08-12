using System.Globalization;

namespace Jellyfin.Plugin.Community.WebIntegration;

internal static class CommunityIndexHtmlTransformer
{
    internal const string MarkerAttribute = "data-jellyfin-community-bootstrap";
    internal const string BootstrapAssetPath = "../Community/assets/communityBootstrap15.js";

    public static string InjectBootstrap(string html, Version version)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(version);

        if (html.Contains(MarkerAttribute, StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var closingBody = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (closingBody < 0)
        {
            return html;
        }

        var versionString = version.ToString();
        var script = string.Format(
            CultureInfo.InvariantCulture,
            "<script {0}=\"{1}\" src=\"{2}?v={1}\" defer></script>",
            MarkerAttribute,
            versionString,
            BootstrapAssetPath);

        return html.Insert(closingBody, script);
    }
}
