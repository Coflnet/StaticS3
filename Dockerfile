FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY "StaticS3.csproj" .
RUN dotnet restore -p:Configuration=Release
COPY . .
RUN dotnet publish "StaticS3.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8000
USER 1000
ENTRYPOINT ["dotnet", "StaticS3.dll", "--hostBuilder:reloadConfigOnChange=false"]
