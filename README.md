# AI Chat API

Lightweight, self-hostable ASP.NET Core API that exposes a JSON `POST /chat` endpoint. Integrates a local knowledge base and is packaged for Docker-based deployment.

## Quick start

Prerequisites: .NET 10 SDK, Docker (optional).

From the project root:

```bash
# Run locally (development)
dotnet build src/Api
dotnet run --project src/Api

# Or with Docker Compose (dev)
docker compose -f infra/docker/compose.dev.yml up --build
```

## Endpoints

- `POST /chat` — submit chat requests (see `Models/ChatRequest.cs`).
- `GET /health` — health/readiness check.

## Notes

- KB: `src/Api/Resources/website_kb.txt` is used by the service for local context.
