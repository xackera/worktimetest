FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/WorkTime.Web/WorkTime.Web.csproj src/WorkTime.Web/
RUN dotnet restore src/WorkTime.Web/WorkTime.Web.csproj
COPY src/WorkTime.Web/ src/WorkTime.Web/
RUN dotnet publish src/WorkTime.Web/WorkTime.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "WorkTime.Web.dll"]
