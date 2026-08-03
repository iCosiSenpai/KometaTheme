using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Web;

/// <summary>
/// Endpoint called by the File Transformation plugin to inject the KometaThemes
/// item-button script tag into the web client's index.html. Anonymous: the
/// File Transformation plugin posts without an auth token.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Plugins/KometaThemes/InjectButton")]
public class KometaWebInjectionController : ControllerBase
{
    private const string MarkerId = "kometathemes-itembutton";

    /// <summary>
    /// Largest document this endpoint will process. Jellyfin's <c>index.html</c> is a few tens of
    /// kilobytes; anything far beyond that is not a real transformation request.
    /// </summary>
    /// <remarks>
    /// This endpoint has to stay anonymous because the File Transformation plugin posts without an
    /// auth token, which means an unauthenticated caller can reach it. Without a cap, a large body
    /// was buffered and then <c>LastIndexOf</c> + <c>Insert</c> allocated another full copy of it,
    /// so a handful of parallel requests could pressure the server's memory for free.
    /// </remarks>
    private const int MaxContentLength = 4 * 1024 * 1024;

    private readonly ILogger<KometaWebInjectionController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaWebInjectionController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public KometaWebInjectionController(ILogger<KometaWebInjectionController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Injects the item-button script tag before &lt;/body&gt; in the posted HTML.
    /// Always returns valid HTML — on any problem the original content is returned
    /// unchanged so the web client can never be broken by this transformation.
    /// </summary>
    /// <param name="request">The transformation request carrying the current file contents.</param>
    /// <returns>The (possibly) modified HTML.</returns>
    [HttpPost]
    [RequestSizeLimit(MaxContentLength)]
    public IActionResult Inject([FromBody] WebTransformationRequest? request)
    {
        var contents = request?.Contents ?? string.Empty;
        try
        {
            if (contents.Length == 0 || contents.Contains(MarkerId, StringComparison.Ordinal))
            {
                return Content(contents, "text/html");
            }

            if (contents.Length > MaxContentLength)
            {
                _logger.LogWarning("Rejecting oversized web transformation payload ({Length} chars)", contents.Length);
                return Content(contents, "text/html");
            }

            var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                // Not an HTML document (e.g. a mis-matched chunk file) — never append
                // a script tag blindly; that would corrupt non-HTML content.
                return Content(contents, "text/html");
            }

            var version = Plugin.Instance?.Version?.ToString() ?? "1";
            var tag = $"<script id=\"{MarkerId}\" defer src=\"/Plugins/KometaThemes/ItemButton.js?v={version}\"></script>";
            return Content(contents.Insert(index, tag), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject item button into index.html; returning original");
            return Content(contents, "text/html");
        }
    }
}
