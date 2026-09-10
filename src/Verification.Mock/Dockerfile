FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY src/Verification.Mock/Verification.Mock.csproj src/Verification.Mock/
RUN dotnet restore src/Verification.Mock/Verification.Mock.csproj

COPY src/Verification.Mock/ src/Verification.Mock/
RUN dotnet publish src/Verification.Mock/Verification.Mock.csproj \
        -c Release -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
USER app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Verification.Mock.dll"]
