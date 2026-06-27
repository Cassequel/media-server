FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY *.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore

COPY . ./
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get update && apt-get install -y ffmpeg
WORKDIR /app
COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "Rockflix.API.dll"]
