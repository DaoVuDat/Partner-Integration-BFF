using System.Text.Json;
using Bff.Api.Contracts;
using Bff.Api.Infrastructure.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Bff.Api.Message;

public class RabbitMqTransactionPublisher: ITransactionPublisher, IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTransactionPublisher> _log;

    // NOTE: The channel is not thread-safe, for simple case, use single channel
    // If we are at higher messages per sec, use pool channel
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqTransactionPublisher(
        IConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTransactionPublisher> log)
        => (_connectionFactory, _options, _log) = (connectionFactory, options.Value, log);

    // PublishAsync will handle multiple requests,
    // And we use one shared RabbitMQ channel ( not recommended but for simplicity for now )
    public async Task PublishAsync(PartnerTransactionAccepted message, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var channel = await EnsureChannelAsync(ct);
            
            // publish message
            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var props = new BasicProperties
            {
                Persistent   = true,                    // survive a broker restart
                MessageId    = message.EventId,         // use PartnerId here
                ContentType  = "application/json",
                Type         = nameof(PartnerTransactionAccepted),
                Timestamp    = new AmqpTimestamp(message.AcceptedAt.ToUnixTimeSeconds())
            };

            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: _options.RoutingKey,
                mandatory: true,
                basicProperties: props,
                body: body,
                cancellationToken: ct);
            
            _log.LogInformation("Published {EventId} for partner {PartnerId}",
                message.EventId, message.PartnerId);
        }
        finally
        {
            _gate.Release();
        }
    }

   
    // PRECONDITION: callers must hold _gate. Two threads in here would race on
    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        // Create RabbitMQ Connection if needed
        _connection ??= await _connectionFactory.CreateConnectionAsync(ct);


        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true, 
                publisherConfirmationTrackingEnabled: true), ct);

        // mandatory: true asks the broker to hand back anything it cannot route,
        // instead of discarding it. Without this handler that return is silent.
        _channel.BasicReturnAsync += (_, ea) =>
        {
            _log.LogError(
                "Unroutable message {MessageId} returned by broker: {ReplyCode} {ReplyText} (exchange {Exchange}, routingKey {RoutingKey})",
                ea.BasicProperties.MessageId, ea.ReplyCode, ea.ReplyText, ea.Exchange, ea.RoutingKey);
            return Task.CompletedTask;
        };

        // Publisher -> Exchange -> Routing -> Queue -> Consumer
        await _channel.ExchangeDeclareAsync(
            _options.Exchange, 
            ExchangeType.Direct, 
            durable: true, 
            cancellationToken: ct);
        
        // We have only queue now, which is about "accepted messages"
        await _channel.QueueDeclareAsync(
            _options.Queue, 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            cancellationToken: ct);
        
        // Binding Queue to Exchange
        await _channel.QueueBindAsync(
            _options.Queue, 
            _options.Exchange, 
            _options.RoutingKey, 
            cancellationToken: ct);

        return _channel;
    }
    
    // Free-up resources
    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.CloseAsync();
        if (_connection is not null) await _connection.CloseAsync();
        _gate.Dispose();
    }
}