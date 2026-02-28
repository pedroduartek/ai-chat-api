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

# AI Chat API

Self-hostable ASP.NET Core chat API that exposes a JSON `POST /chat` endpoint, augments prompts with a local knowledge base, and is packaged for container deployment.

## 🌟 Features

- API-first `POST /chat` for conversational requests
- Lightweight `GET /health` readiness probe
- Local KB support (`Resources/website_kb.txt`) for contextual augmentation
- Container-friendly: Dockerfile and compose manifests for dev/prod

## 🛠️ Tech Stack

- .NET 10 / ASP.NET Core (C#)
- Docker & Docker Compose
- Caddy (reverse proxy) in infra compose

## 🚀 Quick Start

### Prerequisites
- .NET 10 SDK
- Docker & Docker Compose (optional)

### Run locally

```bash
# Build and run the API
dotnet build src/Api
dotnet run --project src/Api
```

### Run with Docker Compose (development)

```bash
docker compose -f infra/docker/compose.dev.yml up --build
```

## 🔌 API Endpoints

- `POST /chat` — submit chat requests (see `src/Api/Models/ChatRequest.cs`)
- `GET /health` — health/readiness check

## 📁 Project Structure

```
ai-chat-api/
├── src/Api/             # ASP.NET Core API
│   ├── Controllers/     # `ChatController`, `HealthController`
│   ├── Services/        # `ChatService`, `IChatService`, options
│   └── Resources/       # local KB (website_kb.txt)
├── infra/docker/        # docker compose and Caddy config
└── docs/                # project docs (includes a generated project doc)
```

## 🤝 Contributing

PRs welcome. Open an issue first for major changes or roadmap discussion.

## 📄 License

Private

test of auto-deploy
