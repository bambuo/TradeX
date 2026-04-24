# Stage 1: Build frontend
FROM node:22-alpine AS frontend-build
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS backend-build
WORKDIR /src
COPY backend/TradeX.Core/TradeX.Core.csproj TradeX.Core/
COPY backend/TradeX.Infrastructure/TradeX.Infrastructure.csproj TradeX.Infrastructure/
COPY backend/TradeX.Exchange/TradeX.Exchange.csproj TradeX.Exchange/
COPY backend/TradeX.Indicators/TradeX.Indicators.csproj TradeX.Indicators/
COPY backend/TradeX.Trading/TradeX.Trading.csproj TradeX.Trading/
COPY backend/TradeX.Notifications/TradeX.Notifications.csproj TradeX.Notifications/
COPY backend/TradeX.Api/TradeX.Api.csproj TradeX.Api/
RUN dotnet restore TradeX.Api/TradeX.Api.csproj

COPY backend/ .
WORKDIR /src/TradeX.Api
RUN dotnet publish -c Release -o /app

# Stage 3: Runtime — single container with API + SPA
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
EXPOSE 80

COPY --from=backend-build /app .
COPY --from=frontend-build /frontend/dist /app/wwwroot

ENTRYPOINT ["dotnet", "TradeX.Api.dll"]
