using Bff.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Bff.UnitTests.Infrastructure;

// The last line of defence: whatever escapes a handler is turned into a ProblemDetails here.
// Its job is to say enough for support to correlate, and nothing more.
public class GlobalExceptionHandlerTests
{
    private sealed record Harness(GlobalExceptionHandler Sut, IProblemDetailsService Service, DefaultHttpContext Context);

    private static Harness Build()
    {
        var service = Substitute.For<IProblemDetailsService>();
        service.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);

        return new Harness(
            new GlobalExceptionHandler(service, NullLogger<GlobalExceptionHandler>.Instance),
            service,
            new DefaultHttpContext { Request = { Method = "POST", Path = "/api/v1/partner/transactions" } });
    }

    private static ProblemDetailsContext WrittenContext(Harness h) =>
        (ProblemDetailsContext)h.Service.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IProblemDetailsService.TryWriteAsync))
            .GetArguments()[0]!;

    [Fact]
    public async Task Maps_an_unexpected_exception_to_500()
    {
        var h = Build();

        var handled = await h.Sut.TryHandleAsync(h.Context, new InvalidOperationException("boom"), default);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, h.Context.Response.StatusCode);
    }

    [Fact]
    public async Task Maps_a_malformed_request_to_400_rather_than_blaming_the_server()
    {
        var h = Build();

        await h.Sut.TryHandleAsync(h.Context, new BadHttpRequestException("unparseable json"), default);

        Assert.Equal(StatusCodes.Status400BadRequest, h.Context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, WrittenContext(h).ProblemDetails.Status);
    }

    [Fact]
    public async Task Does_not_leak_the_exception_message_to_the_caller()
    {
        var h = Build();

        await h.Sut.TryHandleAsync(h.Context, new InvalidOperationException("connection string: user=admin"), default);

        var problem = WrittenContext(h).ProblemDetails;
        Assert.Equal("An unexpected error occurred.", problem.Title);
        Assert.Null(problem.Detail);   // ProblemDetails.Detail is where a stack trace would surface
    }

    [Fact]
    public async Task Carries_a_trace_id_so_a_support_ticket_can_be_matched_to_a_log_line()
    {
        var h = Build();
        h.Context.TraceIdentifier = "trace-123";

        await h.Sut.TryHandleAsync(h.Context, new InvalidOperationException("boom"), default);

        var traceId = Assert.Contains("traceId", WrittenContext(h).ProblemDetails.Extensions);
        Assert.False(string.IsNullOrWhiteSpace(traceId as string));
    }

    [Fact]
    public async Task Writes_nothing_when_the_caller_hung_up()
    {
        var h = Build();
        var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        h.Context.RequestAborted = aborted.Token;

        var handled = await h.Sut.TryHandleAsync(h.Context, new OperationCanceledException(), default);

        // The socket is gone: writing a body would throw, and logging it as an error is noise.
        Assert.True(handled);
        await h.Service.DidNotReceiveWithAnyArgs().TryWriteAsync(default!);
    }

    [Fact]
    public async Task Still_reports_a_cancellation_that_the_caller_did_not_cause()
    {
        var h = Build();   // RequestAborted is NOT cancelled

        await h.Sut.TryHandleAsync(h.Context, new OperationCanceledException(), default);

        // An internal timeout that surfaces as OCE is a real fault, not a client disconnect.
        Assert.Equal(StatusCodes.Status500InternalServerError, h.Context.Response.StatusCode);
        await h.Service.Received(1).TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
