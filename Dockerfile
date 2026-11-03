# Estágio 1: Build do Frontend (Angular)
FROM node:22-alpine AS build-frontend
WORKDIR /app/frontend

COPY frontend/package*.json ./
RUN npm install --legacy-peer-deps

COPY frontend/ ./
RUN npm run build -- --configuration production

# Estágio 2: Build do Backend (.NET 9)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-backend
WORKDIR /app/backend

COPY backend/*.csproj ./
RUN dotnet restore

COPY backend/ ./
# Copiar o build do Angular compilado para dentro do backend
COPY --from=build-frontend /app/backend/wwwroot ./wwwroot

RUN dotnet publish -c Release -o /app/publish

# Estágio 3: Runtime Final (.NET 9 ASP.NET Core)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build-backend /app/publish ./
RUN mkdir -p /app/Data && chmod -R 777 /app/Data

# Porta padrão do ambiente
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "backend.dll"]

