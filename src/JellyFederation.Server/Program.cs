using JellyFederation.Server.Data;
using JellyFederation.Server.Filters;
using JellyFederation.Server.Hubs;
using JellyFederation.Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<FederationDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=federation.db"));

builder.Services.AddSignalR();
builder.Services.AddSingleton<ServerConnectionTracker>();
builder.Services.AddScoped<ApiKeyAuthFilter>();

// CORS: allow the frontend dev server and any configured origin.
// In production, set AllowedOrigins in appsettings.json.
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:4173"];

builder.Services.AddCors(opt => opt.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials())); // required for SignalR

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // serve JS/CSS/assets before routing touches the request
app.UseCors(); // must come before MapControllers / MapHub
app.MapControllers();
app.MapHub<FederationHub>("/hubs/federation");
app.MapFallbackToFile("index.html"); // SPA fallback for client-side routes

app.Run();
