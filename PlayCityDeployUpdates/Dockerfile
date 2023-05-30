FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["PlayCityDeployUpdates/PlayCityDeployUpdates.csproj", "PlayCityDeployUpdates/"]
RUN dotnet restore "PlayCityDeployUpdates/PlayCityDeployUpdates.csproj"
COPY . .
WORKDIR "/src/PlayCityDeployUpdates"
RUN dotnet build "PlayCityDeployUpdates.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PlayCityDeployUpdates.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PlayCityDeployUpdates.dll"]
