# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ITask.Api/ITask.Api.csproj src/ITask.Api/
RUN dotnet restore src/ITask.Api/ITask.Api.csproj
COPY src/ src/
RUN dotnet publish src/ITask.Api/ITask.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
# Non-root user provided by the base image.
USER $APP_UID
ENTRYPOINT ["dotnet", "ITask.Api.dll"]
