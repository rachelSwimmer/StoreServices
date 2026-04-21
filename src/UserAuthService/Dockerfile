FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/SharedKernel/SharedKernel.csproj src/SharedKernel/
COPY src/UserAuthService/UserAuthService.csproj src/UserAuthService/
RUN dotnet restore src/UserAuthService/UserAuthService.csproj

COPY src/SharedKernel/ src/SharedKernel/
COPY src/UserAuthService/ src/UserAuthService/
RUN dotnet publish src/UserAuthService/UserAuthService.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "UserAuthService.dll"]
