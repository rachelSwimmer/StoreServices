# Use the official .NET 8.0 runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 7000

# Use the SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/ApiGateway/ApiGateway.csproj", "ApiGateway/"]
COPY ["src/SharedKernel/SharedKernel.csproj", "SharedKernel/"]
RUN dotnet restore "ApiGateway/ApiGateway.csproj"

# Copy everything else and build
COPY src/ApiGateway/ ApiGateway/
COPY src/SharedKernel/ SharedKernel/
WORKDIR "/src/ApiGateway"
RUN dotnet build "ApiGateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ApiGateway.dll"]