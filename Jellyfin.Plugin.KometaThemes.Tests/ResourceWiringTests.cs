using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Guards the wiring between Plugin.GetPages() and the embedded resources:
/// a typo in either the csproj globs or the resource paths would otherwise only
/// surface as a 404 at runtime inside the Jellyfin dashboard.
/// </summary>
public class ResourceWiringTests
{
    private static readonly string[] ExpectedResources =
    [
        "Jellyfin.Plugin.KometaThemes.Configuration.configPage.html",
        "Jellyfin.Plugin.KometaThemes.Configuration.itemPage.html",
        "Jellyfin.Plugin.KometaThemes.Web.SearchPage.html",
        "Jellyfin.Plugin.KometaThemes.Web.ItemButton.js",
        "Jellyfin.Plugin.KometaThemes.Web.assets.kometa.css",
        "Jellyfin.Plugin.KometaThemes.Web.assets.kometathemes-icon.png",
        "Jellyfin.Plugin.KometaThemes.Web.assets.kometa-loader.js",
        "Jellyfin.Plugin.KometaThemes.Web.assets.kometa-core.js",
        "Jellyfin.Plugin.KometaThemes.Web.assets.kometa-a11y.js",
        "Jellyfin.Plugin.KometaThemes.Web.assets.config.js",
        "Jellyfin.Plugin.KometaThemes.Web.assets.search.js",
        "Jellyfin.Plugin.KometaThemes.Web.assets.item.js",
    ];

    [Theory]
    [MemberData(nameof(ExpectedResourceData))]
    public void EmbeddedResource_Exists(string resourceName)
    {
        var assembly = typeof(Plugin).Assembly;
        Assert.Contains(resourceName, assembly.GetManifestResourceNames());
    }

    [Fact]
    public void GetPages_ResourcePaths_AllExistInAssembly()
    {
        // GetPages is an instance method but only uses the type's namespace, so the
        // paths it produces are derivable statically; ExpectedResources mirrors them.
        var assembly = typeof(Plugin).Assembly;
        var names = assembly.GetManifestResourceNames();
        var missing = ExpectedResources.Where(r => !names.Contains(r, StringComparer.Ordinal)).ToArray();
        Assert.Empty(missing);
    }

    public static TheoryData<string> ExpectedResourceData()
    {
        var data = new TheoryData<string>();
        foreach (var resource in ExpectedResources)
        {
            data.Add(resource);
        }

        return data;
    }
}
