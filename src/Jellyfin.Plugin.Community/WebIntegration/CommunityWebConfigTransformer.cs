using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.Community.WebIntegration;

internal static class CommunityWebConfigTransformer
{
    internal const string ForumMenuName = "Foro";
    internal const string ForumMenuMarker = "Community/app";

    public static string AddForumMenuLink(string json, Version version)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(version);

        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("Jellyfin Web config.json must contain a JSON object.");

        var links = root["menuLinks"] as JsonArray;
        if (links is null)
        {
            links = [];
            root["menuLinks"] = links;
        }

        for (var index = links.Count - 1; index >= 0; index--)
        {
            if (links[index] is not JsonObject link)
            {
                continue;
            }

            var name = link["name"]?.GetValue<string>();
            var url = link["url"]?.GetValue<string>();
            if (string.Equals(name, ForumMenuName, StringComparison.OrdinalIgnoreCase)
                || url?.Contains(ForumMenuMarker, StringComparison.OrdinalIgnoreCase) == true)
            {
                links.RemoveAt(index);
            }
        }

        links.Insert(0, new JsonObject
        {
            ["name"] = ForumMenuName,
            ["icon"] = "forum",
            ["url"] = $"../Community/app?v={version}"
        });

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
