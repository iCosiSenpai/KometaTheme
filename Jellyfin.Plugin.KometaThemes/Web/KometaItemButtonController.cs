using System;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Web;

/// <summary>
/// Serves the KometaThemes item button JavaScript snippet.
/// Users can add this as a custom CSS/JS injection via any injector plugin
/// by pointing to: /Plugins/KometaThemes/ItemButton.js.
/// </summary>
[ApiController]

// Deliberately anonymous, matching KometaWebInjectionController: this script is injected into the
// web client shell before any session exists, so it cannot carry a token. It only returns a static
// embedded asset. Declaring the intent keeps it from looking like a missing [Authorize].
[AllowAnonymous]
[Route("Plugins/KometaThemes/ItemButton.js")]
public class KometaItemButtonController : ControllerBase
{
    private readonly ILogger<KometaItemButtonController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaItemButtonController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public KometaItemButtonController(ILogger<KometaItemButtonController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the item button JavaScript.
    /// </summary>
    /// <returns>JavaScript content.</returns>
    [HttpGet]
    [Produces("application/javascript")]
    public IActionResult GetScript()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        var resourceName = $"{typeof(KometaItemButtonController).Namespace}.ItemButton.js";
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _logger.LogWarning("KometaThemes item button script not found as embedded resource");
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        return Content(content, "application/javascript", Encoding.UTF8);
    }
}
