using ITask.Api.Common;

var builder = WebApplication.CreateBuilder(args);

builder.AddITaskServices();

var app = builder.Build();

await app.UseITaskPipelineAsync();

await app.RunAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
