FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY src/Bff.Api/Bff.Api.csproj src/Bff.Api/
RUN dotnet restore src/Bff.Api/Bff.Api.csproj

COPY src/Bff.Api/ src/Bff.Api/
RUN dotnet publish src/Bff.Api/Bff.Api.csproj \
        -c Release -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

USER app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Bff.Api.dll"]
