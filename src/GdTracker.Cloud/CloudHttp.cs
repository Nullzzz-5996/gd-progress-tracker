using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Cloud;

/// <summary>
/// Общая обвязка HTTP-вызовов сервера: сборка адреса, заголовок авторизации,
/// разбор ответа и превращение кодов ошибок в <see cref="CloudException"/>.
/// Ею пользуются оба транспорта — синхронизация прогресса (<see cref="CloudClient"/>)
/// и демонлист (<see cref="CommunityClient"/>), поэтому сбой сервера и там и там
/// выглядит для интерфейса одинаково.
/// </summary>
internal static class CloudHttp
{
    /// <summary>
    /// Собирает адрес эндпоинта. Адрес сервера пользователь вводит руками, поэтому
    /// хвостовой слэш добавляется здесь: без него <see cref="Uri"/> отбросил бы
    /// последний сегмент базового пути.
    /// </summary>
    public static Uri Endpoint(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new CloudException(CloudErrorKind.Validation, "Не задан адрес сервера синхронизации.");

        var normalized = baseUrl.Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var root)
            || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
        {
            throw new CloudException(
                CloudErrorKind.Validation,
                $"Адрес сервера «{baseUrl}» не похож на ссылку вида https://example.com.");
        }

        return new Uri(root, path);
    }

    public static void Authorize(HttpRequestMessage request, string accessToken)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudException(CloudErrorKind.Network, $"Сервер синхронизации недоступен: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Отмена не по запросу вызывающего — это таймаут HttpClient.
            throw new CloudException(CloudErrorKind.Network, "Сервер синхронизации не ответил вовремя.", ex);
        }
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var error = await TryReadErrorAsync(response, ct);
        var message = error?.Message;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new CloudException(
                CloudErrorKind.Unauthorized, message ?? "Нужен вход в аккаунт."),
            HttpStatusCode.Conflict when error?.Code == "revision_conflict" => new CloudException(
                CloudErrorKind.Conflict, message ?? "В облаке появились изменения с другого устройства."),
            HttpStatusCode.Conflict => new CloudException(
                CloudErrorKind.Validation, message ?? "Запрос отклонён сервером."),
            HttpStatusCode.BadRequest => new CloudException(
                CloudErrorKind.Validation, message ?? "Сервер отклонил данные запроса."),
            HttpStatusCode.TooManyRequests => new CloudException(
                CloudErrorKind.Validation, "Слишком много попыток подряд — попробуй через минуту."),
            _ => new CloudException(
                CloudErrorKind.Server, message ?? $"Сервер ответил ошибкой {(int)response.StatusCode}."),
        };
    }

    private static async Task<ApiError?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiError>(CloudJson.Options, ct);
        }
        catch (Exception e) when (e is JsonException or HttpRequestException or NotSupportedException or TaskCanceledException)
        {
            // Тело ошибки бывает не JSON (например, страница прокси) — тогда остаётся
            // только код статуса, и это лучше, чем падение при разборе ответа.
            return null;
        }
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(CloudJson.Options, ct)
                   ?? throw new CloudException(CloudErrorKind.Server, "Сервер вернул пустой ответ.");
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw new CloudException(CloudErrorKind.Server, "Не удалось разобрать ответ сервера.", e);
        }
    }
}
