# syntax=docker/dockerfile:1

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the build configuration first so restore layers cache on dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .editorconfig ./

# Copy just the project files needed by the web app's graph, then restore.
COPY src/TickForge.Core/TickForge.Core.csproj       src/TickForge.Core/
COPY src/TickForge.Latency/TickForge.Latency.csproj src/TickForge.Latency/
COPY src/TickForge.Feeds/TickForge.Feeds.csproj     src/TickForge.Feeds/
COPY src/TickForge.Web/TickForge.Web.csproj         src/TickForge.Web/
RUN dotnet restore src/TickForge.Web/TickForge.Web.csproj

# Copy the rest of the sources and publish.
COPY src/ src/
RUN dotnet publish src/TickForge.Web/TickForge.Web.csproj -c Release -o /app --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app ./

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    TICKFORGE_SYMBOL=BTC/USDT \
    TICKFORGE_EXCHANGES=binance,coinbase

# Run as the image's built-in non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "TickForge.Web.dll"]
