using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MetricsApp;
using Confluent.Kafka;
using Contracts;
using Contracts.Serializers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme);
builder.Services.AddAuthorizationBuilder();

builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("AppDb"));

builder.Services.AddIdentityCore<MyUser>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddApiEndpoints();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddKafkaProducer<Null, WeatherForecast>("kafka", producerBuilder => {
    producerBuilder.SetValueSerializer(new AvroSerializer<WeatherForecast>());
});

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapFallbackToFile("index.html");

var api = app.MapGroup("/api");
api.MapIdentityApi<MyUser>();
api.MapClientApi(app.Configuration);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(); // UseSwaggerUI Protected by if (app.Environment.IsDevelopment())
    
   app.UseWebAssemblyDebugging();
}

app.UseStaticFiles();
app.UseBlazorFrameworkFiles();

await Auth.InitializeTestUserAsync(app.Services);

app.Run();

