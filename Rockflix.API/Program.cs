using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Rockflix.API.Data;
using Rockflix.API.Services;
using Rockflix.API.Services.Filtering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// PostgreSQL + EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
        // Allow token via query string for video streaming (video tags can't set headers)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["token"].ToString();
                if (!string.IsNullOrEmpty(token))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Services
builder.Services.AddOptions<EncodingOptions>()
    .Bind(builder.Configuration.GetSection(EncodingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<TmdbService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<MediaScannerService>();
builder.Services.AddSingleton<MediaRequestService>();
builder.Services.AddSingleton<StreamCacheService>();
builder.Services.AddSingleton<DoesTheDogDieService>();
builder.Services.AddSingleton<CleanStreamService>();
builder.Services.AddSingleton<MovieTimestampService>();
builder.Services.AddHttpClient("cleanstream", client => client.MaxResponseContentBufferSize = 2 * 1024 * 1024)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient("timestamp-identity").RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<FilterRequestService>();
builder.Services.AddScoped<RadarrAcquisition>();
builder.Services.AddSingleton<MediaProcessor>();
builder.Services.AddHostedService<MovieFilterWorker>();
builder.Services.AddHttpClient("doesthedogdie").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Named HTTP clients for Radarr and Sonarr
builder.Services.AddHttpClient("radarr", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Radarr:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["Radarr:ApiKey"]!);
});
builder.Services.AddHttpClient("sonarr", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Sonarr:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["Sonarr:ApiKey"]!);
});
builder.Services.AddHttpClient("telegram");

// CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:5174",
                "http://localhost:3000",
                "https://watch.aidenswanson.com"
              )
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Apply pending migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
