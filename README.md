# AI Chat API

Backend service for pedroduartek.com. This is a self-hosted ASP.NET Core API
that handles website chat, streaming chat responses, contact-email delivery,
and readiness checks. Chat responses are grounded with a local knowledge base
and served through an Ollama-backed model.

## Stack

- .NET 10 / ASP.NET Core
- Ollama with `llama3.2:1b` by default
- MailKit
- Serilog
- Polly
- Docker Compose + Caddy

## Local development

Prerequisites: .NET 10 SDK. Docker is optional for local containers and Ollama.

```bash
dotnet build ai-chat-api.sln
dotnet run --project src/Api
```

Run with Docker Compose:

```bash
docker compose -f infra/docker/compose.dev.yml up --build
```

## Repository layout

```text
ai-chat-api/
├── src/Api/
│   ├── Application/     # contracts and typed request models
│   ├── Controllers/     # HTTP endpoints
│   ├── Infrastructure/  # external adapters
│   ├── Options/         # typed configuration
│   ├── Resources/       # website knowledge base
│   ├── Security/        # request guardrails
│   └── Services/        # chat, email, and warmup workflows
├── tests/Api.Tests/     # xUnit test suite
└── infra/docker/        # compose and Caddy config
```

## Quality gates

```bash
dotnet test tests/Api.Tests/Api.Tests.csproj
dotnet build ai-chat-api.sln
```

## License

Proprietary. All rights reserved. See [LICENSE](LICENSE).
