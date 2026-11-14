FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BackendDotnet.csproj .
RUN dotnet restore BackendDotnet.csproj
COPY . .
RUN dotnet publish BackendDotnet.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1
USER nobody
ENTRYPOINT ["dotnet", "BackendDotnet.dll"]
