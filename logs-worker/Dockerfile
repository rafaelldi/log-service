FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["logs-worker/logs-worker.csproj", "logs-worker/"]
RUN dotnet restore "logs-worker/logs-worker.csproj"
COPY . .
WORKDIR "/src/logs-worker"
RUN dotnet build "logs-worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "logs-worker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "logs-worker.dll"]
