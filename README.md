# AI Chat API

Self-hostable ASP.NET Core API for Pedro Duarte's website. The application exposes chat, health, and contact-email endpoints, augments chat prompts with a local knowledge base, and ships with Docker-based dev and production deployment files.

## Features

- `POST /chat` for synchronous chat responses
- `POST /chat/stream` for streaming chat responses
- `POST /email` for contact-email delivery through SMTP
- `GET /health` for readiness checks
- Local knowledge base support via `src/Api/Resources/website_kb.txt`
- Docker Compose manifests for development and production

## Tech stack

- .NET 10 / ASP.NET Core
- MailKit for SMTP delivery
- Serilog for structured logging
- Polly for outbound HTTP retry policy
- Docker and Caddy for containerized deployment

## Quick start

Prerequisites: .NET 10 SDK and optionally Docker.

Run locally:

```bash
dotnet build ai-chat-api.sln
dotnet run --project src/Api
```

Run with Docker Compose:

```bash
docker compose -f infra/docker/compose.dev.yml up --build
```

Run tests:

```bash
dotnet test tests/Api.Tests/Api.Tests.csproj
```

## Project structure

```text
ai-chat-api/
├── src/Api/
│   ├── Application/     # contracts and typed LLM request models
│   ├── Controllers/     # HTTP endpoints
│   ├── Infrastructure/  # external adapters (HTTP, file system)
│   ├── Options/         # typed configuration objects
│   ├── Security/        # request guardrails
│   ├── Services/        # chat, email, and warmup workflows
│   └── Resources/       # local knowledge base content
├── tests/Api.Tests/     # xUnit test suite
└── infra/docker/        # compose and reverse-proxy config
```

## License

Proprietary. All rights reserved. See [LICENSE](C:/Users/pduarte/repos/Project%202026/ai-chat-api/LICENSE).
