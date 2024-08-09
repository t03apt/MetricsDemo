using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Contracts;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace MetricsApp;

public static class ClientApi
{
    private static readonly string[] s_summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    public static void MapClientApi(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        endpoints.MapGet("weather", async (IProducer<Null, WeatherForecast> producer) =>
        {
            await Task.Delay(Random.Shared.Next(1000));

            if (Random.Shared.Next(5) == 0)
            {
                throw new InvalidOperationException("Error getting weather data.");
            }

            var results = Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.Date.AddDays(index),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = s_summaries[Random.Shared.Next(s_summaries.Length)]
            }).ToArray();


            foreach (var forecast in results)
            {
                var message = new Message<Null, WeatherForecast>
                {
                    Value = new WeatherForecast
                    {
                        Date = forecast.Date,
                        Summary = forecast.Summary,
                        TemperatureC = forecast.TemperatureC
                    }
                };

                var propagationContext = Activity.Current != null ? new PropagationContext(Activity.Current.Context, Baggage.Current) : default;
                if (propagationContext != default)
                {
                    Propagators.DefaultTextMapPropagator.Inject(propagationContext, message, InjectTraceContext);
                }

                await producer.ProduceAsync(KafkaTopics.Forecast, message);
            }

            return results.Select(WeatherForecastViewModel.ConvertFromWeatherForecast);
        }).RequireAuthorization();

        endpoints.MapGet("startup", () =>
        {
            return new
            {
                GrafanaUrl = (string)configuration["GRAFANA_URL"]!
            };
        });
    }

    private static void InjectTraceContext<TKey, TValue>(Message<TKey, TValue> message, string name, string value)
    {
        message.Headers ??= new Headers();
        message.Headers.Add(name, Encoding.UTF8.GetBytes(value));
    }

    private sealed class WeatherForecastViewModel
    {
        public DateOnly Date { get; set; }
        public int TemperatureC { get; set; }
        public string? Summary { get; set; }
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        public static WeatherForecastViewModel ConvertFromWeatherForecast(WeatherForecast forecast)
        {
            return new WeatherForecastViewModel
            {
                Date = DateOnly.FromDateTime(forecast.Date),
                Summary = forecast.Summary,
                TemperatureC = forecast.TemperatureC
            };
        }
    }
}
