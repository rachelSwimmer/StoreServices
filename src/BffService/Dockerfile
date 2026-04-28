FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/SharedKernel/SharedKernel.csproj src/SharedKernel/
COPY src/BffService/BffService.csproj src/BffService/
RUN dotnet restore src/BffService/BffService.csproj

COPY src/SharedKernel/ src/SharedKernel/
COPY src/BffService/ src/BffService/
RUN dotnet publish src/BffService/BffService.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "BffService.dll"]
