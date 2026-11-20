# DrugExplorer Microservices

This repository contains the incremental microservices implementation for DrugExplorer. The existing backend remains a separate project and is not part of this repository.

## Architecture documentation

- [Migration plan](docs/microservices-architecture-plan.md)
- [Architecture diagrams](docs/microservices-architecture-diagrams.md)
- [Azure Service Bus guide](docs/microservices-azure-service-bus-guide.md)
- [Quick reference](docs/MICROSERVICES-QUICK-REFERENCE.md)

## Provider profiles

| Runtime | Embeddings and chat | Vector search and indexing |
|---|---|---|
| Local development and tests | Ollama | Qdrant |
| Azure deployment | Azure OpenAI Service | Azure AI Search |

Qdrant and Ollama are local-only. Tests run locally and must not require live Azure services. Azure adapters are planned; the documentation distinguishes this target from the current backend implementation.

## GitHub Actions and deployments

Keep the services in this repository initially, with independent CI/CD workflows per service. Azure infrastructure and shared platform resources should be defined as code alongside the service deployment configuration.

## Current solution

- `src/DrugExplorer.Contracts` contains versioned inter-service message contracts.
- `src/DrugExplorer.ProviderAbstractions` contains provider-neutral AI and vector-store interfaces.
- `src/DrugExplorer.Providers.Ollama` contains local Ollama embedding and chat adapters.
- `src/DrugExplorer.Providers.Qdrant` contains local Qdrant indexing and search adapters.
- `src/DrugExplorer.Providers.DependencyInjection` selects and registers the configured local providers.
- `tests/DrugExplorer.Contracts.Tests` verifies contract serialization without contacting Azure.
- `tests/DrugExplorer.Providers.Ollama.Tests` verifies the Ollama HTTP mappings with a local stub handler.
- `tests/DrugExplorer.Providers.Qdrant.Tests` verifies Qdrant index and search HTTP mappings with a local stub handler.
- `tests/DrugExplorer.Providers.DependencyInjection.Tests` verifies provider selection and registration.

## Local provider configuration

The current build supports the local providers only; selecting Azure OpenAI or Azure AI Search fails explicitly until those adapters are implemented. Configure the providers in the application configuration:

```json
{
  "Providers": {
    "VectorStore": "Qdrant",
    "Ai": "Ollama"
  },
  "Qdrant": {
    "BaseUrl": "http://localhost:6333/",
    "CollectionName": "drug_chunks",
    "VectorSize": 768
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434/",
    "EmbeddingModel": "nomic-embed-text",
    "ChatModel": "llama3.2"
  }
}
```

Set the Ollama model names to models available in the local Ollama installation.

Register the providers from the application's composition root:

```csharp
builder.Services.AddDrugExplorerProviders(builder.Configuration);
```

Run the full local test suite from the repository root:

```powershell
dotnet restore .\DrugExplorer.Microservices.sln --source https://api.nuget.org/v3/index.json
dotnet test .\DrugExplorer.Microservices.sln --no-restore
```
