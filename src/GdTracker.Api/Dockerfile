# Сервер синхронизации GD Progress Tracker.
# Собирается из корня репозитория:
#   docker build -f src/GdTracker.Api/Dockerfile -t gdtracker-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только файлы проектов: слой с восстановлением пакетов переиспользуется,
# пока не менялись зависимости.
COPY Directory.Build.props ./
COPY src/GdTracker.Api/GdTracker.Api.csproj src/GdTracker.Api/
COPY src/GdTracker.Sharing/GdTracker.Sharing.csproj src/GdTracker.Sharing/
COPY src/GdTracker.Core/GdTracker.Core.csproj src/GdTracker.Core/
RUN dotnet restore src/GdTracker.Api/GdTracker.Api.csproj

COPY src/GdTracker.Api/ src/GdTracker.Api/
COPY src/GdTracker.Sharing/ src/GdTracker.Sharing/
COPY src/GdTracker.Core/ src/GdTracker.Core/
RUN dotnet publish src/GdTracker.Api/GdTracker.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# База SQLite и ключ подписи токенов живут в томе: без него аккаунты исчезали бы
# при каждом перезапуске контейнера.
ENV Data__Directory=/data
VOLUME /data

# Порт по умолчанию; хостинги переопределяют его переменной PORT.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "GdTracker.Api.dll"]
