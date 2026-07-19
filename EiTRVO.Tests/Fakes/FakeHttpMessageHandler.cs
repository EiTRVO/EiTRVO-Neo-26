using System.Net;

namespace EiTRVO.Tests.Fakes;

/// <summary>
/// Test-only HttpMessageHandler. Supports four modes:
/// 1. Single static response: new FakeHttpMessageHandler(statusCode, json)
/// 2. Single exception: new FakeHttpMessageHandler(exception)
/// 3. Queue-based sequence: new FakeHttpMessageHandler((503,""), (200,"ok"))
/// 4. URL-based mapping: FakeHttpMessageHandler.ForUrlMap().Map(...).Build()
///
/// All modes record every request in <see cref="Requests"/>.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    /// <summary>All requests sent through this handler (in order).</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    // ---- Constructor 1: single static response ----
    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseJson)
        : this(_ => new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(responseJson)
        })
    { }

    // ---- Constructor 2: single static exception ----
    public FakeHttpMessageHandler(Exception exception)
        : this(_ => throw exception)
    { }

    // ---- Constructor 3: queue-based sequence ----
    public FakeHttpMessageHandler(params (HttpStatusCode StatusCode, string Json)[] responses)
        : this(CreateQueueHandler(responses))
    { }

    /// <summary>Creates the queue-based handler. Last response repeats when queue is exhausted.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> CreateQueueHandler(
        (HttpStatusCode StatusCode, string Json)[] responses)
    {
        if (responses.Length == 0)
            throw new ArgumentException("At least one response required", nameof(responses));

        var queue = new Queue<(HttpStatusCode, string)>(responses);
        (HttpStatusCode StatusCode, string Json)? last = null;

        return _ =>
        {
            if (queue.Count > 0)
                last = queue.Dequeue();

            var r = last!.Value;
            return new HttpResponseMessage
            {
                StatusCode = r.StatusCode,
                Content = new StringContent(r.Json)
            };
        };
    }

    // ---- Core constructor ----
    private FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    // ---- SendAsync ----
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_handler(request));
    }

    // ---- URL-based mapping (factory) ----
    /// <summary>
    /// Start building a URL-mapped handler.
    /// Example:
    /// <code>
    /// var handler = FakeHttpMessageHandler.ForUrlMap()
    ///     .Map("primary", HttpStatusCode.NotFound, "")
    ///     .Map("fallback", HttpStatusCode.OK, "data")
    ///     .Build();
    /// </code>
    /// </summary>
    public static UrlMapBuilder ForUrlMap() => new();

    public class UrlMapBuilder
    {
        private readonly List<(string UrlContains, Func<HttpResponseMessage> Factory)> _rules = new();

        /// <summary>Match requests whose URL contains <paramref name="urlContains"/>.</summary>
        public UrlMapBuilder Map(string urlContains, HttpStatusCode statusCode, string body)
        {
            _rules.Add((urlContains, () => new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(body)
            }));
            return this;
        }

        /// <summary>Match requests whose URL contains <paramref name="urlContains"/> — throw instead.</summary>
        public UrlMapBuilder MapThrow(string urlContains, Exception exception)
        {
            _rules.Add((urlContains, () => throw exception));
            return this;
        }

        public FakeHttpMessageHandler Build()
        {
            var rules = _rules.ToList(); // capture snapshot
            return new FakeHttpMessageHandler(req =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                foreach (var (contains, factory) in rules)
                {
                    if (url.Contains(contains, StringComparison.OrdinalIgnoreCase))
                        return factory();
                }
                // Fallback: 200 OK with empty body
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("")
                };
            });
        }
    }
}
