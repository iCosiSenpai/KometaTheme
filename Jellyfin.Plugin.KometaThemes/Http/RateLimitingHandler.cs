using System;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Http;

/// <summary>
/// HTTP handler that enforces a token bucket rate limit to avoid overwhelming the AnimeThemes API.
/// The bucket is re-evaluated on every request so changes to <see cref="Configuration.PluginConfiguration.RateLimitPerMinute"/>
/// from the dashboard take effect immediately.
/// </summary>
public sealed class RateLimitingHandler : DelegatingHandler
{
    /// <summary>
    /// Lowest accepted request rate.
    /// </summary>
    public const int MinRatePerMinute = 1;

    /// <summary>
    /// Highest accepted request rate. animethemes.moe publishes a 90 req/min budget; the
    /// configuration is clamped to the same ceiling so the dashboard cannot advertise a rate this
    /// handler would silently cap.
    /// </summary>
    public const int MaxRatePerMinute = 90;

    private readonly ILogger<RateLimitingHandler> _logger;
    private readonly object _limiterLock = new();
#pragma warning disable CA2213
    private TokenBucketRateLimiter _limiter;
#pragma warning restore CA2213
    private int _currentRatePerMinute;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public RateLimitingHandler(ILogger<RateLimitingHandler> logger)
    {
        _logger = logger;
        _currentRatePerMinute = Math.Clamp(Plugin.Instance?.Configuration?.RateLimitPerMinute ?? 60, MinRatePerMinute, MaxRatePerMinute);
        _limiter = BuildLimiter(_currentRatePerMinute);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Capture the limiter once. Reading the field twice let a concurrent reconfigure swap and
        // dispose the instance between the acquire and its use, surfacing as ObjectDisposedException
        // on in-flight requests whenever the rate limit was edited during a sync.
        var limiter = EnsureLimiterMatchesConfig();

        using var lease = await AcquireWithRetryAsync(limiter, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            _logger.LogError("Rate limit still exceeded after retry");

            // A bare InvalidOperationException is not classified as a transient HTTP fault, so the
            // resilience pipeline would not retry it and the whole resolve chain failed hard.
            // HttpRequestException is retryable and is what callers already expect.
            throw new HttpRequestException("KometaThemes rate limit exceeded: the request queue is saturated.");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RateLimitLease> AcquireWithRetryAsync(RateLimiter limiter, CancellationToken cancellationToken)
    {
        var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (lease.IsAcquired)
        {
            return lease;
        }

        lease.Dispose();
        _logger.LogWarning("Rate limit exceeded, waiting for token availability");
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        return await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
    }

    private TokenBucketRateLimiter EnsureLimiterMatchesConfig()
    {
        var newRate = Math.Clamp(Plugin.Instance?.Configuration?.RateLimitPerMinute ?? 60, MinRatePerMinute, MaxRatePerMinute);
        if (newRate == Volatile.Read(ref _currentRatePerMinute))
        {
            return _limiter;
        }

        lock (_limiterLock)
        {
            if (newRate == _currentRatePerMinute)
            {
                return _limiter;
            }

            // The superseded limiter is deliberately not disposed: requests may still be queued on
            // it, and disposing a TokenBucketRateLimiter cancels their leases. It only owns a timer,
            // which stops once the instance becomes unreachable.
            _limiter = BuildLimiter(newRate);
            Volatile.Write(ref _currentRatePerMinute, newRate);
            _logger.LogInformation("Rate limit reconfigured to {Rate} req/min", newRate);
            return _limiter;
        }
    }

    private static TokenBucketRateLimiter BuildLimiter(int ratePerMinute)
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = ratePerMinute,
            ReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / ratePerMinute),
            TokensPerPeriod = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 100,
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// Disposes the rate limiter.
    /// </summary>
    /// <param name="disposing">Whether we're disposing.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Read once: a concurrent reconfigure may replace the field.
            _limiter.Dispose();
        }

        base.Dispose(disposing);
    }
}
