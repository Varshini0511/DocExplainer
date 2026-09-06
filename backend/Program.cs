using MyFirstAI.Controllers;
using MyFirstAI.Services;

var builder = WebApplication.CreateBuilder(args);

// ===================================================
// SERVICES REGISTRATION
// Same pattern as registering DbContext, Identity etc.
// ===================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>();
builder.Services.AddHttpClient<RagController>();
builder.Services.AddHttpClient<IAgentService, AgentService>();
// ---------------------------------------------------
// Register Gemini service
// API key comes from appsettings.Development.json or environment variables
// Get your free key at: https://aistudio.google.com/app/apikey
// ---------------------------------------------------
builder.Services.AddScoped<IGeminiService, GeminiService>();
// ---------------------------------------------------
// CORS - allow Angular dev server to call this API
// ---------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ===================================================
// MIDDLEWARE PIPELINE
// ===================================================

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MyFirstAI v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAngular");
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
