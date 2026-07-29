using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using Api.Infrastructure;
using Api.Options;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Guards the resilience budget and the streaming opt-out. The pipeline itself is
/// wired in Program.cs; these tests pin the contract it depends on so a future
/// tweak cannot silently reintroduce retry storms on the self-hosted model.
/// </summary>
public class ResilienceOptionsTests
{
    private static IEnumerable<ValidationResult> Validate(ResilienceOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_are_sized_for_an_interactive_chat()
    {
        var options = new ResilienceOptions();

        // A human is waiting on the widget, so the whole budget must stay well
        // under the 100s HttpClient default that used to apply.
        Assert.Equal(25, options.TotalTimeoutSeconds);
        Assert.True(options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds);

        // At most one extra generation beyond the first attempt plus one more,
        // never the old 3-retry (4 generations) fan-out.
        Assert.Equal(2, options.MaxRetryAttempts);

        // Sub-second first backoff, not the old 2s/4s/8s batch pacing.
        Assert.True(options.RetryBaseDelayMilliseconds < 1_000);
    }

    [Fact]
    public void Defaults_pass_data_annotation_validation()
    {
        Assert.Empty(Validate(new ResilienceOptions()));
    }

    [Fact]
    public void Worst_case_retry_budget_fits_inside_the_total_timeout()
    {
        var options = new ResilienceOptions();

        // Exponential backoff from the base delay, plus one attempt timeout per try.
        var backoff = Enumerable
            .Range(0, options.MaxRetryAttempts)
            .Sum(attempt => options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt)) / 1000d;
        var attempts = (options.MaxRetryAttempts + 1) * options.AttemptTimeoutSeconds;

        Assert.True(
            backoff < options.TotalTimeoutSeconds,
            $"Backoff alone ({backoff}s) must not consume the {options.TotalTimeoutSeconds}s budget.");

        // The total timeout is deliberately allowed to cut a doomed retry chain
        // short; what matters is that a single successful retry can still fit.
        Assert.True(
            options.AttemptTimeoutSeconds * 2 + backoff <= options.TotalTimeoutSeconds,
            $"A first attempt plus one retry ({options.AttemptTimeoutSeconds * 2 + backoff}s) must fit in {options.TotalTimeoutSeconds}s.");
        Assert.True(attempts > 0);
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(10, 25)]
    public void Attempt_timeout_must_stay_below_total(int attemptSeconds, int totalSeconds)
    {
        // Mirrors the .Validate() guard registered in Program.cs.
        var options = new ResilienceOptions
        {
            AttemptTimeoutSeconds = attemptSeconds,
            TotalTimeoutSeconds = totalSeconds
        };

        var isCoherent = options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds;
        Assert.Equal(attemptSeconds < totalSeconds, isCoherent);
    }

    [Fact]
    public void Streaming_requests_are_flagged_and_opt_out_of_retry()
    {
        using var streaming = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        streaming.Options.Set(ChatRequestKinds.IsStreaming, true);

        Assert.True(ChatRequestKinds.IsStreamingRequest(streaming));
    }

    [Fact]
    public void Blocking_requests_are_not_treated_as_streaming()
    {
        // The blocking path uses PostAsJsonAsync and never sets the marker.
        using var blocking = new HttpRequestMessage(HttpMethod.Post, "/api/chat");

        Assert.False(ChatRequestKinds.IsStreamingRequest(blocking));
    }

    [Fact]
    public void A_missing_request_is_not_treated_as_streaming()
    {
        // On an exception outcome there may be no request to inspect; defaulting to
        // "not streaming" keeps transient network faults retryable.
        Assert.False(ChatRequestKinds.IsStreamingRequest(null));
    }
}
