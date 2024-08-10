using System.Diagnostics;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts;
using Contracts.Serializers;
using MassTransit;
using MassTransit.Monitoring;
using MassTransitConsumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Context.Propagation;

var builder = Host.CreateApplicationBuilder(args);

var kafkaBroker = Environment.GetEnvironmentVariable("ConnectionStrings__kafka");

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(Instrumentation.ActivitySource.Name);
    });

builder.Services.Configure<InstrumentationOptions>(options =>
{
    // options.ReceiveTotal = "mt.receive.total";
});

builder.Services.AddMassTransit(x =>
{
    x.UsingInMemory();

    x.AddReceiveObserver<ActivityEnricherReceiveObserver>();

    x.AddRider(rider =>
    {
        rider.AddConsumer<ConsumerWorker>();

        rider.UsingKafka((context, k) =>
        {
            k.ClientId = "Consumer";
            k.Host(kafkaBroker);

            k.TopicEndpoint<WeatherForecast>(KafkaTopics.ForecastsInCelsius, "my-consumer-group", e =>
            {
                e.SetValueDeserializer(new AvroSerializer<WeatherForecast>());
                e.ConfigureConsumer<ConsumerWorker>(context);
            });
        });

        rider.AddProducer<WeatherForecast>(KafkaTopics.ForecastsInFahrenheit, (context, cfg) =>
        {
            cfg.SetValueSerializer(new AvroSerializer<WeatherForecast>());
        });
    });
});

await EnsureTopicExists(kafkaBroker, KafkaTopics.ForecastsInCelsius, KafkaTopics.ForecastsInFahrenheit);

await builder.Build().RunAsync();

static async Task<IAdminClient> EnsureTopicExists(string? kafkaBroker, params string[] topics)
{
    using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafkaBroker }).Build();
    foreach (var topic in topics)
    {
        var spec = new TopicSpecification { Name = topic, ReplicationFactor = 1, NumPartitions = 1 };
        try
        {
            await adminClient.CreateTopicsAsync([spec]);
        }
        catch (CreateTopicsException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    return adminClient;
}

sealed class ConsumerWorker(ILogger<ConsumerWorker> logger, ITopicProducer<WeatherForecast> topicProducer) : IConsumer<WeatherForecast>
{
    private readonly Random random = new();

    public async Task Consume(ConsumeContext<WeatherForecast> context)
    {
        logger.LogInformation("Received message: {Message}", context.Message);

        var propagationContext = ExtractPropagationContext(context.Headers);
        using var activity = Instrumentation.ActivitySource.StartActivity("SomeWork", ActivityKind.Internal, propagationContext.ActivityContext);
        if (context.TryGetHeader<string>(CustomHeaders.TenantId, out var tenantId))
        {
            activity?.SetTag("custom.tenantid", tenantId);
        }

        // wait like we were doing something with the message
        await Task.Delay(random.Next(100, 1000));

        if (context.Message.Unit != TemperatureUnit.Celsius)
        {
            throw new InvalidOperationException();
        }

        await topicProducer.Produce(new WeatherForecast
        {
            Date = context.Message.Date,
            Summary = context.Message.Summary,
            Temperature = 32 + (int)(context.Message.Temperature / 0.5556),
            Unit = context.Message.Unit
        });
    }

    private static PropagationContext ExtractPropagationContext(MassTransit.Headers metadata)
        => Propagators.DefaultTextMapPropagator.Extract(default, metadata, ExtractTraceContext);

    private static IEnumerable<string> ExtractTraceContext(MassTransit.Headers metadata, string name)
    {
        if (metadata.TryGetHeader(name, out var value) && value != null && value is string stringValue)
        {
            yield return stringValue;
        }
    }
}

class ActivityEnricherReceiveObserver : IReceiveObserver
{
    public Task ConsumeFault<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception) where T : class => Task.CompletedTask;

    public async Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType) where T : class
    {
        await SetCustomTagsOnCurrentActivity(context.Headers);
    }

    public async Task PostReceive(ReceiveContext context)
    {
        await SetCustomTagsOnCurrentActivity(context.TransportHeaders);
    }

    public Task PreReceive(ReceiveContext context)
    {
        if (context is KafkaConsumeContext kafkaContext)
        {
            context.AddMetricTags("topic", kafkaContext.Topic);
        }

        if (Activity.Current != null)
        {
            var propagationContext = ExtractPropagationContext(context.TransportHeaders);
            Activity.Current.SetParentId(propagationContext.ActivityContext.TraceId, propagationContext.ActivityContext.SpanId);
            foreach (var item in propagationContext.Baggage)
            {
                Activity.Current.SetBaggage(item.Key, item.Value);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReceiveFault(ReceiveContext context, Exception exception) => Task.CompletedTask;

    private static Task SetCustomTagsOnCurrentActivity(MassTransit.Headers headers)
    {
        var tenantId = headers.Get<string>(CustomHeaders.TenantId);
        if (tenantId != null)
        {
            Activity.Current?.SetTag("custom.tenantid", "tenantId");
        }
        return Task.CompletedTask;
    }

    private static PropagationContext ExtractPropagationContext(MassTransit.Headers metadata)
        => Propagators.DefaultTextMapPropagator.Extract(default, metadata, ExtractTraceContext);

    private static IEnumerable<string> ExtractTraceContext(MassTransit.Headers metadata, string name)
    {
        if (metadata.TryGetHeader(name, out var value) && value != null && value is string stringValue)
        {
            yield return stringValue;
        }
    }
}
