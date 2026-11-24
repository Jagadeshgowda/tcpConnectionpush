using TCPCommunication.Configuration;
using TCPCommunication.Data;
using TCPCommunication.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
builder.Services.Configure<TcpAcquisitionOptions>(
    builder.Configuration.GetSection(TcpAcquisitionOptions.SectionName));

// ─── Infrastructure ──────────────────────────────────────────────────────────
builder.Services.AddSingleton<DeviceReadingRepository>();
builder.Services.AddSingleton<FileOutputService>();

// ─── Core acquisition service (hosted) ───────────────────────────────────────
// Register as both IHostedService and as a concrete singleton so the
// controller can inject it directly for monitoring calls.
builder.Services.AddSingleton<DeviceManagerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceManagerService>());

// ─── Web API ──────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "TCP Device Acquisition API",
        Version     = "v1",
        Description = "Monitor and query data from TCP-connected industrial devices."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
