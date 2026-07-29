using System.ComponentModel.DataAnnotations;

namespace Api.Options;

/// <summary>
/// Tuning for the outbound resilience pipeline in front of the LLM.
/// The defaults are sized for an interactive chat widget: a human is waiting,
/// so the budget favours failing fast over exhausting every retry.
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// Hard ceiling for one inbound chat request, retries included.
    /// </summary>
    [Range(5, 300)]
    public int TotalTimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// Ceiling for a single attempt against the LLM. Must stay below
    /// <see cref="TotalTimeoutSeconds"/> so a retry can still fit in the budget.
    /// </summary>
    [Range(2, 300)]
    public int AttemptTimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// Retries after the first attempt. Only transient failures are retried, and
    /// never a streaming request: replaying a generation that already produced
    /// output wastes the model for nothing.
    /// </summary>
    [Range(0, 5)]
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Base delay for the exponential backoff. Jitter is always applied on top.
    /// </summary>
    [Range(50, 10_000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 300;

    /// <summary>
    /// Share of failed calls inside the sampling window that trips the breaker.
    /// </summary>
    [Range(0.1, 1.0)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Sampling window for the breaker's failure ratio.
    /// </summary>
    [Range(5, 600)]
    public int CircuitBreakerSamplingSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum calls in the window before the breaker may trip, so a couple of
    /// early failures on a quiet site do not open the circuit.
    /// </summary>
    [Range(2, 100)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 6;

    /// <summary>
    /// How long the circuit stays open before probing the LLM again.
    /// </summary>
    [Range(1, 600)]
    public int CircuitBreakerDurationSeconds { get; set; } = 15;
}
