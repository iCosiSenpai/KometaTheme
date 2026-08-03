using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Jellyfin.Plugin.KometaThemes.Http;

/// <summary>
/// Resilience handler that uses Polly under the hood with retry, circuit breaker, and timeout.
/// </summary>
public sealed class PollyResilienceHandler : DelegatingHandler
{
    /// <summary>
    /// Budget for a single attempt. This covers response headers plus, for streamed downloads, the
    /// body read — so it must be generous enough for a full theme video.
    /// </summary>
    private const int PerAttemptTimeoutSeconds = 300;

    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly ILogger<PollyResilienceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollyResilienceHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PollyResilienceHandler(ILogger<PollyResilienceHandler> logger)
    {
        _logger = logger;

        // Order matters: in Polly v8 the first strategy added is the OUTERMOST one. The timeout used
        // to be registered first, so a single 30s budget wrapped all three retries plus ~14s of
        // exponential backoff — retries were usually cut off before they could run, and the
        // resulting TimeoutRejectedException is not classified as transient so it escaped raw.
        // Retry now wraps timeout, giving every individual attempt its own budget.
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(GetRetryOptions())
            .AddCircuitBreaker(GetCircuitBreakerOptions())
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(PerAttemptTimeoutSeconds),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Request attempt timed out after {Timeout}s", args.Timeout.TotalSeconds);
                    return default;
                }
            })
            .Build();
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token).ConfigureAwait(false),
            cancellationToken).AsTask();
    }

    private static ValueTask<bool> HandleTransientHttpError(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException } => PredicateResult.True(),
        { Result.StatusCode: HttpStatusCode.RequestTimeout } => PredicateResult.True(),
        { Result.StatusCode: HttpStatusCode.TooManyRequests } => PredicateResult.True(),
        { Result.StatusCode: >= HttpStatusCode.InternalServerError } => PredicateResult.True(),
        _ => PredicateResult.False()
    };

    private RetryStrategyOptions<HttpResponseMessage> GetRetryOptions() => new()
    {
        ShouldHandle = args => HandleTransientHttpError(args.Outcome),
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),
        DelayGenerator = args => ValueTask.FromResult(
            args.Outcome.Result?.Headers.RetryAfter?.Delta),
        OnRetry = args =>
        {
            _logger.LogWarning(
                "Retry {AttemptNumber} for request after {Delay}ms. Status: {Status}",
                args.AttemptNumber,
                args.RetryDelay.TotalMilliseconds,
                args.Outcome.Result?.StatusCode);

            // The failed response is about to be replaced by a fresh attempt; without this its
            // content stream and the connection it holds leak once per retry.
            args.Outcome.Result?.Dispose();
            return default;
        }
    };

    private CircuitBreakerStrategyOptions<HttpResponseMessage> GetCircuitBreakerOptions() => new()
    {
        ShouldHandle = args => HandleTransientHttpError(args.Outcome),
        FailureRatio = 0.8,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30),
        OnOpened = args =>
        {
            _logger.LogError(
                "Circuit breaker opened for {Duration}s due to too many failures",
                args.BreakDuration.TotalSeconds);
            return default;
        },
        OnClosed = _ =>
        {
            _logger.LogInformation("Circuit breaker closed, resuming normal operation");
            return default;
        }
    };
}
