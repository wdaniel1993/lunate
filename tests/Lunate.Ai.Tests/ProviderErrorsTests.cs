using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Anthropic.Exceptions;

namespace Lunate.Ai.Tests;

public sealed class ProviderErrorsTests
{
    [Fact]
    public void Timeouts_are_retryable()
    {
        Assert.True(ProviderErrors.IsRetryable(new TaskCanceledException("timed out")));
        Assert.True(ProviderErrors.IsRetryable(new TimeoutException("timed out")));
    }

    [Fact]
    public void Transport_failures_are_retryable()
    {
        Assert.True(ProviderErrors.IsRetryable(new HttpRequestException("connection reset")));
    }

    [Fact]
    public void Wrapped_transport_failures_are_retryable()
    {
        Assert.True(
            ProviderErrors.IsRetryable(
                new InvalidOperationException("outer", new HttpRequestException("connection reset"))
            )
        );
    }

    [Fact]
    public void Transient_client_result_statuses_are_retryable()
    {
        foreach (int status in new[] { 408, 429, 500, 502, 503, 504, 529 })
        {
            Assert.True(
                ProviderErrors.IsRetryable(new ClientResultException(new FakeResponse(status))),
                $"status {status} should be retryable"
            );
        }
    }

    [Fact]
    public void Permanent_client_result_statuses_are_not_retryable()
    {
        Assert.False(ProviderErrors.IsRetryable(new ClientResultException(new FakeResponse(400))));
        Assert.False(ProviderErrors.IsRetryable(new ClientResultException(new FakeResponse(401))));
        Assert.False(ProviderErrors.IsRetryable(new ClientResultException(new FakeResponse(404))));
    }

    [Fact]
    public void Anthropic_status_carriers_follow_the_same_status_rule()
    {
        Assert.True(ProviderErrors.IsRetryable(AnthropicError(HttpStatusCode.TooManyRequests)));
        Assert.True(ProviderErrors.IsRetryable(AnthropicError(HttpStatusCode.BadGateway)));
        Assert.False(ProviderErrors.IsRetryable(AnthropicError(HttpStatusCode.BadRequest)));
        Assert.False(ProviderErrors.IsRetryable(AnthropicError(HttpStatusCode.Unauthorized)));
    }

    [Fact]
    public void Ordinary_failures_are_not_retryable()
    {
        Assert.False(ProviderErrors.IsRetryable(new InvalidOperationException("bad request")));
        Assert.False(ProviderErrors.IsRetryable(new ArgumentException("bad argument")));
    }

    private static AnthropicApiException AnthropicError(HttpStatusCode status) =>
        new("provider error", null!) { StatusCode = status, ResponseBody = "{}" };

    private sealed class FakeResponse(int status) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override BinaryData Content => BinaryData.FromString(string.Empty);

        public override Stream? ContentStream { get; set; } = Stream.Null;

        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) =>
            Content;

        public override ValueTask<BinaryData> BufferContentAsync(
            CancellationToken cancellationToken = default
        ) => new(Content);

        public override void Dispose() { }
    }
}
