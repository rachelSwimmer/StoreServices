FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/SharedKernel/SharedKernel.csproj src/SharedKernel/
COPY src/ProductCatalogService/ProductCatalogService.csproj src/ProductCatalogService/
RUN dotnet restore src/ProductCatalogService/ProductCatalogService.csproj

COPY src/SharedKernel/ src/SharedKernel/
COPY src/ProductCatalogService/ src/ProductCatalogService/
RUN dotnet publish src/ProductCatalogService/ProductCatalogService.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ProductCatalogService.dll"]
