FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet publish argus.Server.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ARGUS_LOG_FORMAT=json
ENV ARGUS_HOST=0.0.0.0
EXPOSE 3001

# Run as an unprivileged user; the server binds 3001 (>1024) and only reads config.
RUN useradd --system --uid 10001 --home-dir /nonexistent --shell /usr/sbin/nologin argus
USER argus

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -fsS http://127.0.0.1:3001/health || exit 1

ENTRYPOINT ["dotnet", "argus.Server.dll"]
