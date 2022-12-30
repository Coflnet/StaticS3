#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Container we use for final publish
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app

# Build container
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

# Copy the code into the container
WORKDIR /build
COPY "StaticS3.csproj" .

# NuGet restore
RUN dotnet restore
COPY . .

# Build the API
RUN dotnet build -c Release -o /app/build

# Publish it
FROM build AS publish
RUN dotnet publish "StaticS3.csproj" -c Release -o /app/publish

# Make the final image for publishing
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app
USER app

ENTRYPOINT ["dotnet", "StaticS3.dll"]
