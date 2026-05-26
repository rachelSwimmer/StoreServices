FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/SharedKernel/SharedKernel.csproj src/SharedKernel/
COPY src/NotificationService/NotificationService.csproj src/NotificationService/
RUN dotnet restore src/NotificationService/NotificationService.csproj

COPY src/SharedKernel/ src/SharedKernel/
COPY src/NotificationService/ src/NotificationService/
RUN dotnet publish src/NotificationService/NotificationService.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "NotificationService.dll"]
