using System.Text.Json;
using Bff.Api.Infrastructure.Options;
using Bff.Api.Message;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;

namespace Bff.UnitTests.Message;

public class RabbitMqTransactionPublisherTests
{
    private static readonly RabbitMqOptions Options = new()
    {
        ConnectionString = "amqp://guest:guest@localhost:5672",
        Exchange         = "partner.transactions",
        Queue            = "partner.transactions.accepted",
        RoutingKey       = "transaction.accepted"
    };

    private sealed record Harness(
        RabbitMqTransactionPublisher Publisher,
        IConnectionFactory Factory,
        IConnection Connection,
        IChannel Channel);

    private static Harness Build()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
                  .Returns(channel);

        var factory = Substitute.For<IConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        var publisher = new RabbitMqTransactionPublisher(
            factory,
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<RabbitMqTransactionPublisher>.Instance);

        return new Harness(publisher, factory, connection, channel);
    }

    private static PartnerTransactionAccepted Event(string reference = "TXN-99823") => new()
    {
        EventId              = $"P-1001:{reference}",
        TransactionReference = reference,
        PartnerId            = "P-1001",
        Amount               = 250.00m,
        Currency             = "USD",
        OccurredAt           = new DateTimeOffset(2026, 5, 10, 14, 30, 0, TimeSpan.Zero),
        AcceptedAt           = new DateTimeOffset(2026, 5, 10, 14, 30, 1, TimeSpan.Zero)
    };

    // Captures the arguments of the single BasicPublishAsync call the publisher made.
    private static (string Exchange, string RoutingKey, bool Mandatory, BasicProperties Props, byte[] Body)
        CapturePublish(IChannel channel)
    {
        var call = channel.ReceivedCalls()
                          .Single(c => c.GetMethodInfo().Name == nameof(IChannel.BasicPublishAsync));
        var args = call.GetArguments();

        return ((string)args[0]!,
                (string)args[1]!,
                (bool)args[2]!,
                (BasicProperties)args[3]!,
                ((ReadOnlyMemory<byte>)args[4]!).ToArray());
    }

    [Fact]
    public async Task Publishes_to_the_configured_exchange_and_routing_key()
    {
        var h = Build();

        await h.Publisher.PublishAsync(Event());

        var (exchange, routingKey, _, _, _) = CapturePublish(h.Channel);
        Assert.Equal(Options.Exchange, exchange);
        Assert.Equal(Options.RoutingKey, routingKey);
    }

    [Fact]
    public async Task Publishes_as_mandatory_so_an_unroutable_message_comes_back()
    {
        var h = Build();

        await h.Publisher.PublishAsync(Event());

        var (_, _, mandatory, _, _) = CapturePublish(h.Channel);
        Assert.True(mandatory);   // otherwise the broker drops it in silence
    }

    [Fact]
    public async Task Marks_the_message_persistent_so_it_survives_a_broker_restart()
    {
        var h = Build();

        await h.Publisher.PublishAsync(Event());

        var (_, _, _, props, _) = CapturePublish(h.Channel);
        Assert.True(props.Persistent);
        Assert.Equal(DeliveryModes.Persistent, props.DeliveryMode);   // delivery-mode 2 on the wire
    }

    [Fact]
    public async Task Carries_the_event_id_as_MessageId_for_consumer_deduplication()
    {
        var h = Build();

        await h.Publisher.PublishAsync(Event("TXN-42"));

        var (_, _, _, props, _) = CapturePublish(h.Channel);
        Assert.Equal("P-1001:TXN-42", props.MessageId);   // at-least-once delivery needs a stable key
        Assert.Equal("application/json", props.ContentType);
        Assert.Equal(nameof(PartnerTransactionAccepted), props.Type);
    }

    [Fact]
    public async Task Serialises_the_event_as_json_without_losing_a_field()
    {
        var h = Build();
        var expected = Event();

        await h.Publisher.PublishAsync(expected);

        var (_, _, _, _, body) = CapturePublish(h.Channel);
        var actual = JsonSerializer.Deserialize<PartnerTransactionAccepted>(body);

        Assert.Equal(expected, actual);          // records compare by value
        Assert.Equal("1.0", actual!.SchemaVersion);
    }

    [Fact]
    public async Task Declares_a_durable_exchange_queue_and_binding_before_publishing()
    {
        var h = Build();

        await h.Publisher.PublishAsync(Event());

        // durable: true on both, autoDelete: false on the queue — the consumer may be offline.
        await h.Channel.Received(1).ExchangeDeclareAsync(
            Options.Exchange, ExchangeType.Direct,
            durable: true, autoDelete: false,
            arguments: Arg.Any<IDictionary<string, object?>>(),
            passive: false, noWait: false, cancellationToken: Arg.Any<CancellationToken>());

        await h.Channel.Received(1).QueueDeclareAsync(
            Options.Queue,
            durable: true, exclusive: false, autoDelete: false,
            arguments: Arg.Any<IDictionary<string, object?>>(),
            passive: false, noWait: false, cancellationToken: Arg.Any<CancellationToken>());

        await h.Channel.Received(1).QueueBindAsync(
            Options.Queue, Options.Exchange, Options.RoutingKey,
            arguments: Arg.Any<IDictionary<string, object?>>(),
            noWait: false, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enables_publisher_confirmations_so_the_publish_awaits_the_broker_ack()
    {
        var h = Build();

        await h.Publisher.PublishAsync(Event());

        await h.Connection.Received(1).CreateChannelAsync(
            Arg.Is<CreateChannelOptions>(o => o.PublisherConfirmationsEnabled
                                           && o.PublisherConfirmationTrackingEnabled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opens_one_connection_and_one_channel_for_many_publishes()
    {
        var h = Build();

        for (var i = 0; i < 5; i++)
            await h.Publisher.PublishAsync(Event($"TXN-{i}"));

        // A connection per publish is the classic socket-exhaustion bug.
        await h.Factory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
        await h.Connection.Received(1).CreateChannelAsync(Arg.Any<CreateChannelOptions>(),
                                                          Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Concurrent_publishes_still_share_a_single_connection()
    {
        var h = Build();

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => h.Publisher.PublishAsync(Event($"TXN-{i}")))));

        await h.Factory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
        Assert.Equal(20, h.Channel.ReceivedCalls()
                                  .Count(c => c.GetMethodInfo().Name == nameof(IChannel.BasicPublishAsync)));
    }

    [Fact]
    public async Task Reconnects_when_the_channel_has_died()
    {
        var h = Build();
        await h.Publisher.PublishAsync(Event("TXN-1"));

        h.Channel.IsOpen.Returns(false);   // broker dropped it between publishes
        await h.Publisher.PublishAsync(Event("TXN-2"));

        await h.Connection.Received(2).CreateChannelAsync(Arg.Any<CreateChannelOptions>(),
                                                          Arg.Any<CancellationToken>());
        await h.Factory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Propagates_a_broker_failure_so_the_caller_cannot_acknowledge_unqueued_work()
    {
        var h = Build();
        h.Channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                                    Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(),
                                    Arg.Any<CancellationToken>())
                 .Returns(ValueTask.FromException(new Exception("broker nacked")));

        await Assert.ThrowsAsync<Exception>(() => h.Publisher.PublishAsync(Event()));
    }

    [Fact]
    public async Task Releases_the_gate_after_a_failure_so_later_publishes_are_not_deadlocked()
    {
        var h = Build();
        h.Channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                                    Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(),
                                    Arg.Any<CancellationToken>())
                 .Returns(ValueTask.FromException(new Exception("transient")),
                          ValueTask.CompletedTask);

        await Assert.ThrowsAsync<Exception>(() => h.Publisher.PublishAsync(Event("TXN-1")));

        // If the finally block were missing, this would hang forever rather than fail.
        var second = h.Publisher.PublishAsync(Event("TXN-2"));
        var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(second, completed);
        await second;
    }

    [Fact]
    public async Task Closes_the_channel_and_connection_on_dispose()
    {
        var h = Build();
        await h.Publisher.PublishAsync(Event());

        await h.Publisher.DisposeAsync();

        await h.Channel.Received(1).CloseAsync(Arg.Any<CancellationToken>());
        await h.Connection.Received(1).CloseAsync(Arg.Any<CancellationToken>());
    }
}
