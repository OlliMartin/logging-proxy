using System.Text;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpLogging(opts =>
{
  opts.RequestBodyLogLimit = int.MaxValue;
  // opts.MediaTypeOptions.AddText("application/json");
});

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddLogging(
  opts =>
  {
    opts.AddOpenTelemetry(
      otelOptions =>
      {
        ResourceBuilder resourceBuilder =
          ResourceBuilder.CreateDefault().AddService(
              "log-rev-proxy",
              "acaad.dev",
              serviceInstanceId: System.Net.Dns.GetHostName()
            )
            .AddEnvironmentVariableDetector();

        otelOptions.SetResourceBuilder(resourceBuilder);

        otelOptions.IncludeScopes = true;
        otelOptions.IncludeFormattedMessage = true;
        otelOptions.ParseStateValues = true;

        otelOptions.AddOtlpExporter();
      }
    );
  }
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
}

app.MapReverseProxy(proxyPipeline =>
{
  proxyPipeline.Use(MyCustomProxyStep);
});

app.UseHttpLogging();

app.Run();

async Task MyCustomProxyStep(HttpContext context, Func<Task> next)
{
  var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
  
  logger.LogInformation("Forwarding request {route}, {traceId}", context.Request.Path, context.TraceIdentifier);

  try
  {
    var memStream = new MemoryStream();
    await context.Request.Body.CopyToAsync(memStream);

    memStream.Seek(0, SeekOrigin.Begin);
    context.Request.Body = memStream;
    
    string content = Encoding.UTF8.GetString(memStream.ToArray());
    logger.LogInformation("REQ {traceId}: {content}, sIp: {sIp}",  context.TraceIdentifier, content, context.Connection.RemoteIpAddress);
  }
  catch (Exception ex)
  {
    logger.LogError(ex, "Could not get request payload");
  }
  
  // Important - required to move to the next step in the proxy pipeline
  await next();
}