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
