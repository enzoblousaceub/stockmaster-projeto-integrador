# Estagio 1: build do frontend (Angular)
FROM node:22-alpine AS build-frontend
WORKDIR /app/frontend

COPY frontend/package*.json ./
# --legacy-peer-deps ainda necessario: conflito de peer entre @angular/core e @angular/service-worker
# (some quando os pacotes @angular/* forem alinhados no mesmo patch). npm ci = instalacao reproduzivel do lockfile.
RUN npm ci --legacy-peer-deps

COPY frontend/ ./
RUN npm run build -- --configuration production

# Estagio 2: build do backend (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-backend
WORKDIR /app/backend

COPY backend/*.csproj ./
RUN dotnet restore

COPY backend/ ./
# Build do Angular compilado para dentro do backend
COPY --from=build-frontend /app/backend/wwwroot ./wwwroot

RUN dotnet publish -c Release -o /app/publish --no-restore

# Estagio 3: runtime (.NET 10, usuario nao-root "app" da imagem)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build-backend /app/publish ./

ENV PORT=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

USER app

# Sem curl na imagem: usa /dev/tcp do bash para consultar /healthz
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/${PORT}; printf "GET /healthz HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3; head -n1 <&3 | grep -q " 200 "'

ENTRYPOINT ["dotnet", "backend.dll"]
