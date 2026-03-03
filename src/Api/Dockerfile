FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Api.csproj", "./"]
RUN dotnet restore "./Api.csproj"
COPY . .
RUN dotnet publish "Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app

# Run as non-root user — limits blast radius if the container is compromised.
RUN useradd --no-create-home --shell /bin/false appuser
USER appuser

COPY --from=build /app/publish .
# Ensure the Resources folder (website_kb.txt) from the project is present in the final image
COPY --from=build /src/Resources ./Resources
ENTRYPOINT ["dotnet", "Api.dll"]
