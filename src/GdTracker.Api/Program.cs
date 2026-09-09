using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GdTracker.Api.Data;
using GdTracker.Api.Endpoints;
using GdTracker.Api.Security;
using GdTracker.Sharing.Cloud;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Локальные настройки этой машины: почта владельца и прочее, чему не место в
// публичном репозитории (файл в .gitignore, на боевом сервере его нет).
//
// Место в цепочке выбрано намеренно — сразу за остальными appsettings, но до
// переменных окружения и аргументов командной строки. Добавленный в конец, файл
// перебивал бы и настройки боевого сервера, и то, что подставляют тесты.
var localSettings = new JsonConfigurationSource
{
    Path = "appsettings.Local.json",
    Optional = true,
    ReloadOnChange = false,
};

var afterJsonFiles = 0;
for (var i = 0; i < builder.Configuration.Sources.Count; i++)
{
    if (builder.Configuration.Sources[i] is JsonConfigurationSource)
        afterJsonFiles = i + 1;
}

builder.Configuration.Sources.Insert(afterJsonFiles, localSettings);

// Хостинги вроде Render и Railway сообщают порт переменной PORT и ждут, что
// приложение слушает его на всех интерфейсах. Явный ASPNETCORE_URLS имеет приоритет.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port)
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Каталог с БД и файлом ключа подписи. По умолчанию — App_Data рядом с сервером;
// в боевой среде задаётся через Data:Directory или переменную окружения Data__Directory.
var dataDirectory = builder.Configuration["Data:Directory"] is { Length: > 0 } configuredDir
    ? configuredDir
    : Path.Combine(builder.Environment.ContentRootPath, "App_Data");

var connectionString = builder.Configuration.GetConnectionString("Db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Directory.CreateDirectory(dataDirectory);
    connectionString = $"Data Source={Path.Combine(dataDirectory, "gdtracker-server.db")}";
}

// Те же настройки JSON, что у клиента: camelCase и перечисления строками.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = CloudJson.Options.PropertyNamingPolicy;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<ApiDbContext>(options => options.UseSqlite(connectionString));

var signingKey = SigningKeyProvider.Resolve(
    builder.Configuration["Auth:SigningKey"] ?? Environment.GetEnvironmentVariable("GDTRACKER_SIGNING_KEY"),
    dataDirectory,
    out var keyWasGenerated);

var issuer = builder.Configuration["Auth:Issuer"] ?? "gdtracker";
var audience = builder.Configuration["Auth:Audience"] ?? "gdtracker-app";
var tokenLifetime = TimeSpan.FromDays(
    double.TryParse(builder.Configuration["Auth:TokenLifetimeDays"], out var days) && days > 0 ? days : 30);

builder.Services.AddSingleton<ITokenIssuer>(
    new JwtTokenIssuer(signingKey, issuer, audience, tokenLifetime));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ValidateLifetime = true,
            // Без этого просроченный на пару минут токен всё ещё принимался бы:
            // по умолчанию допускается расхождение часов в пять минут.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// Права модератора и администратора выдаются из конфигурации, а не регистрацией.
builder.Services.AddSingleton(new RoleBootstrap(builder.Configuration));

// Страница демонлиста лежит на другом домене (GitHub Pages), поэтому браузеру
// нужен явный список разрешённых источников. Задаётся через Cors:Origins;
// по умолчанию — сайт проекта и локальная отладка.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").GetChildren()
    .Select(c => c.Value)
    .Where(v => !string.IsNullOrWhiteSpace(v))
    .Select(v => v!.TrimEnd('/'))
    .ToArray();

if (corsOrigins.Length == 0)
{
    corsOrigins =
    [
        "https://nullzzz-5996.github.io",
        "http://localhost:5500",
        "http://127.0.0.1:5500",
    ];
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Ограничение частоты обращений к входу и регистрации: перебор паролей должен
// упираться в лимит, а не в скорость сети.
var authPermitPerMinute = int.TryParse(builder.Configuration["RateLimiting:AuthPermitPerMinute"], out var permit) && permit > 0
    ? permit
    : 20;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Пакет прогресса — это JSON со всеми уровнями и попытками; десяти мегабайт
// хватает с запасом, а неограниченный размер тела — приглашение к DoS.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();

    // EnsureCreated поднимает только новую базу. На сервере, который работал
    // до появления демонлиста, база уже есть — добираем в ней недостающее.
    await SchemaUpgrader.UpgradeAsync(db, app.Logger);

    // Пустой демонлист наполняется стартовой расстановкой из глобального списка;
    // уже заполненный не трогаем, чтобы не затереть работу модераторов.
    var seeded = await DemonListSeeder.SeedAsync(db);
    if (seeded > 0)
        app.Logger.LogInformation("Демонлист наполнен стартовой расстановкой: {Count} позиций.", seeded);

    var promoted = await scope.ServiceProvider.GetRequiredService<RoleBootstrap>().ApplyToExistingAsync(db);
    if (promoted > 0)
        app.Logger.LogInformation("Права из настроек выданы аккаунтам: {Count}.", promoted);
}

if (keyWasGenerated)
{
    app.Logger.LogWarning(
        "Ключ подписи токенов не задан в конфигурации (Auth:SigningKey) — используется файл {Path}. " +
        "Для боевой среды задай ключ явно.",
        Path.Combine(dataDirectory, "signing.key"));
}

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapSyncEndpoints();
app.MapCommunityEndpoints();

app.Run();

/// <summary>
/// Явный частичный класс точки входа: интеграционные тесты поднимают сервер
/// через WebApplicationFactory&lt;Program&gt;, которому нужен доступный тип Program.
/// </summary>
public partial class Program;
