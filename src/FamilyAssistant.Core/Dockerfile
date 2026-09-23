FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/FamilyAssistant.Core/ src/FamilyAssistant.Core/
COPY tests/FamilyAssistant.Core.Tests/ tests/FamilyAssistant.Core.Tests/
FROM build AS test
RUN dotnet restore tests/FamilyAssistant.Core.Tests/FamilyAssistant.Core.Tests.csproj
ENTRYPOINT ["dotnet", "test", "tests/FamilyAssistant.Core.Tests/FamilyAssistant.Core.Tests.csproj", "--no-restore"]
FROM build AS publish
RUN dotnet publish src/FamilyAssistant.Core/FamilyAssistant.Core.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /out .
RUN mkdir -p /app/secrets/google && chown -R app:app /app/secrets/google
RUN mkdir -p /app/data && chown app:app /app/data && chmod 700 /app/data
ENV ASPNETCORE_HTTP_PORTS=8080
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "FamilyAssistant.Core.dll"]
