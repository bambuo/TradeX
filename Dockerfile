# Stage 1: Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Stage 2: Restore + Build backend (shared)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS backend-build
WORKDIR /src
COPY backend/TradeX.Core/TradeX.Core.csproj TradeX.Core/
COPY backend/TradeX.Infrastructure/TradeX.Infrastructure.csproj TradeX.Infrastructure/
COPY backend/TradeX.Exchange/TradeX.Exchange.csproj TradeX.Exchange/
COPY backend/TradeX.Indicators/TradeX.Indicators.csproj TradeX.Indicators/
COPY backend/TradeX.Trading/TradeX.Trading.csproj TradeX.Trading/
COPY backend/TradeX.Notifications/TradeX.Notifications.csproj TradeX.Notifications/
COPY backend/TradeX.Api/TradeX.Api.csproj TradeX.Api/
COPY backend/TradeX.Worker/TradeX.Worker.csproj TradeX.Worker/
RUN dotnet restore TradeX.Api/TradeX.Api.csproj && \
    dotnet restore TradeX.Worker/TradeX.Worker.csproj
COPY backend/ .

# Stage 3a: Publish API (includes SPA static files)
FROM backend-build AS api-publish
WORKDIR /src/TradeX.Api
RUN dotnet publish -c Release -o /app

# Stage 3b: Publish Worker
FROM backend-build AS worker-publish
WORKDIR /src/TradeX.Worker
RUN dotnet publish -c Release -o /app

# Stage 4a: Runtime — API (default target)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS api
WORKDIR /app
EXPOSE 80
COPY --from=api-publish /app .
COPY --from=frontend-build /frontend/dist /app/wwwroot
ENTRYPOINT ["dotnet", "TradeX.Api.dll"]

# Stage 4b: Runtime — Worker
FROM mcr.microsoft.com/dotnet/runtime:10.0-preview AS worker
WORKDIR /app
# Prometheus metrics endpoint
EXPOSE 9464
COPY --from=worker-publish /app .
ENTRYPOINT ["dotnet", "TradeX.Worker.dll"]
