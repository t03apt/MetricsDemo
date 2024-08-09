﻿var builder = DistributedApplication.CreateBuilder(args);

builder.AddOpenTelemetryCollector("collector", "./otel-collector/config.yaml")
    .WithEndpoint(8889, 8889)
    .WithAppForwarding();

var grafana = builder.AddContainer("grafana", "grafana/grafana")
                     .WithBindMount("./grafana/data/", "/var/lib/grafana")
                     .WithBindMount("./grafana/config", "/etc/grafana", isReadOnly: true)
                     .WithBindMount("./grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
                     .WithHttpEndpoint(targetPort: 3000, name: "http");

builder.AddContainer("prometheus", "prom/prometheus")
       .WithBindMount("./prometheus", "/etc/prometheus", isReadOnly: true)
       .WithHttpEndpoint(/* This port is fixed as it's referenced from the Grafana config */ port: 9090, targetPort: 9090);

var kafka = builder
    .AddKafka("kafka")
    .WithKafkaUI();

builder.AddProject<Projects.MetricsApp>("app")
       .WithEnvironment("GRAFANA_URL", grafana.GetEndpoint("http"))
       .WithReference(kafka);

using var app = builder.Build();

await app.RunAsync();
