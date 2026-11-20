# DrugExplorer Microservices Migration Plan

## 1. Current system

The backend is a layered .NET application:

- `DrugExplorer.Api` exposes the HTTP API.
- `DrugExplorer.Application` contains orchestration and application services.
- `DrugExplorer.Domain` contains domain models.
- `DrugExplorer.Persistence` owns EF Core and SQL Server access.
- `DrugExplorer.Infrastructure` integrates with OpenFDA and currently contains the local AI and vector-store adapters.

The existing application interfaces (`IEmbeddingService`, `IAIModelClientService`, `IVectorStore`, and `IVectorIndexWriter`) are provider boundaries. Qdrant and Ollama are for local development and local testing only; they are not Azure deployment targets.

The main capabilities are drug search and ingestion, semantic search, RAG answers, vector-index building, medicament seeding, and analytics. The current API is `DrugsController`; it exposes search, semantic search, custom-question, seed-medicaments, and build-vector-index endpoints.

## 2. Target architecture

Begin with a **modular monolith and strangler migration**, not a simultaneous rewrite. Extract a service only when it has a clear business boundary, independent data ownership, and an operational reason to deploy or scale independently.

### Proposed services

1. **API Gateway** — retain a single client-facing entry point. Route HTTP requests to services, enforce shared cross-cutting policies, and preserve the existing public API during migration.
2. **Drug Catalog and Search** — own drug records and OpenFDA search/ingestion. Keep search requests synchronous over HTTP because callers need the results immediately.
3. **Knowledge Indexing Worker** — own chunk creation, embedding generation, and vector-index writes. Run long-running indexing and seeding work asynchronously.
4. **Semantic Search** — own query embedding and vector retrieval.
5. **RAG Answering** — own retrieval, relevance filtering, prompt construction, and answer generation. It may call the semantic-search API synchronously where a response is required.
6. **Analytics** — consume integration events and own analytics read models. Extract this only if analytics needs independent scaling or isolation.

Do not create a separate deployment for every class or endpoint by default. Keep the current layered projects as implementation boundaries while measuring the cost and benefits of each extraction.

## 3. Service boundaries and data ownership

- Assign each service one authoritative data store. Other services must use its API or published events rather than reading or writing its tables directly.
- Start by isolating ownership in the existing SQL Server deployment if separate databases are not yet practical; move to separate databases as services are extracted.
- The Drug Catalog owns drug source records. The Knowledge Indexing Worker owns indexing job state and its SQL metadata. A provider-neutral vector-search abstraction hides the selected store.
- Analytics owns its projections and must not become a dependency for user-facing search or RAG requests.
- Use versioned API contracts and integration-event schemas; avoid sharing persistence entities across services.

## 4. Local and Azure AI/vector providers

| Runtime | Embeddings and chat | Vector search and indexing | Test scope |
|---|---|---|---|
| Local development | Ollama | Qdrant | Run unit and integration tests locally against local dependencies |
| Azure deployment | Azure OpenAI Service | Azure AI Search | Keep tests local; do not require live Azure services |

Implement adapters for both provider sets behind the existing application interfaces. Select them through environment-specific dependency injection and configuration; do not make Application or Domain depend on provider SDK types. Preserve vector payload, identifier, and retrieval contracts where supported, and explicitly map provider-specific scoring and filtering behavior.

The Azure adapters are target work, not capabilities currently implemented by this backend. Keep Azure endpoints, deployment/model identifiers, API versions, and credentials in configuration or managed identity; do not reuse local Ollama/Qdrant settings for Azure.

## 5. Azure Service Bus design

Use Azure Service Bus for **asynchronous commands and integration events**, not for request/response search endpoints.

Initial flows:

| Producer | Message | Consumer | Purpose |
|---|---|---|---|
| Drug Catalog | `DrugIngested` | Knowledge Indexing Worker | Index newly stored or updated drug label data |
| API / operations endpoint | `BuildIndexRequested` | Knowledge Indexing Worker | Start a full or scoped index build |
| Seeding workflow | `SeedingCompleted` | Analytics | Record completion and operational metrics |
| User-facing services | `SearchCompleted`, `RagAnswerCompleted` | Analytics | Populate analytics projections |

Use topics with independent subscriptions when multiple consumers need the same event; use queues for work handled by one competing-consumer group. Define entities and subscriptions through infrastructure as code.

Message handling requirements:

- Include a stable message ID, event type and schema version, UTC timestamp, and correlation/trace ID.
- Make handlers idempotent; expect at-least-once delivery.
- Use bounded retries and exponential backoff for transient failures. Dead-letter poison or invalid messages, with documented inspection and replay procedures.
- Use the transactional outbox when a database update must reliably produce an event. Do not publish an event before its corresponding database transaction is committed.
- Configure message TTL, lock renewal, concurrency, duplicate detection, and sessions only where ordering or workload requirements justify them.
- Use managed identity and least-privilege Azure RBAC. Keep secrets out of source control.

## 6. Migration phases

### Phase 0 — Baseline and contracts

1. Document endpoint behavior, data ownership, dependencies, and current operational constraints.
2. Add correlation IDs, structured logs, health checks, and baseline latency/error metrics.
3. Define service contracts and event schemas before moving code.
4. Confirm deployment, database, and Azure Service Bus cost constraints.

**Exit criteria:** baseline tests and metrics exist; proposed boundaries and event contracts are reviewed.

### Phase 1 — Asynchronous indexing

1. Extract the existing background ingestion and vector-index build workflows into a worker process.
2. Add `BuildIndexRequested` and `DrugIngested` messages, initially using a single queue or topic/subscription as required by consumers.
3. Implement idempotency, retries, dead-letter handling, and an outbox where database updates publish events.
4. Keep current HTTP endpoints as compatibility adapters that submit work and return a job identifier/status where needed.

**Exit criteria:** indexing can be monitored, retried, and replayed without duplicate or inconsistent data.

### Phase 2 — Drug Catalog and Search

1. Define ownership of drug records and OpenFDA ingestion.
2. Extract the catalog/search API and its persistence boundary.
3. Publish integration events only after successful persistence.
4. Route existing search traffic through the gateway and compare results with the monolith.

**Exit criteria:** existing search behavior is preserved and the service owns its data access.

### Phase 3 — Semantic Search and RAG

1. Extract semantic retrieval behind a stable HTTP contract.
2. Extract RAG answering separately only if its deployment/scaling needs justify the additional service boundary.
3. Preserve current similarity filtering and source attribution behavior.
4. Add timeouts, cancellation, and dependency health checks for the selected embedding and vector-search providers.

**Exit criteria:** API compatibility and answer grounding are verified by integration tests.

### Phase 4 — Analytics and retirement

1. Build analytics projections from versioned events.
2. Verify projections against the existing analytics behavior.
3. Gradually shift traffic, monitor error rates and latency, and retain a rollback path.
4. Remove obsolete monolith paths only after traffic and data reconciliation succeed.

**Exit criteria:** each extracted service is independently deployable and observable; old paths are safely retired.

## 7. Deployment and operations

- Prefer the simplest hosting option that meets the team's scaling and availability requirements; evaluate Azure Container Apps before adopting AKS unless Kubernetes capabilities are specifically needed.
- Provision Service Bus, Azure AI Search, Azure OpenAI Service, identities, network access, monitoring, and application hosting using Bicep or Terraform.
- Store secrets in Azure Key Vault; prefer managed identities for Service Bus, SQL, and other supported Azure resources.
- Use Application Insights / Azure Monitor for distributed tracing, structured logs, dependency telemetry, and alerts.
- Track request latency and errors, queue depth and age, retries and dead letters, indexing throughput/failures, and Azure AI Search/Azure OpenAI dependency health.
- Deploy services independently through CI/CD; define health probes, resource limits, rollout checks, and rollback procedures.

## 8. Testing and acceptance

- Unit-test business rules and event handlers.
- Integration-test service APIs, persistence boundaries, and Service Bus behavior.
- Test duplicate delivery, transient outages, poison messages, cancellation, and dead-letter replay.
- Add end-to-end tests for search, ingestion-to-indexing, semantic search, and RAG.
- Run all automated and integration tests locally. Use local Qdrant/Ollama for provider integration tests and local fakes/mocks for Azure adapters; tests must not require deployed Azure AI services.
- Compare migrated endpoint responses and indexing results with the current backend before shifting production traffic.
- Review Azure AI Search and Azure OpenAI configuration, quotas, and costs before deployment; live Azure testing is outside this plan's test scope.

## 9. Key risks and mitigations

| Risk | Mitigation |
|---|---|
| Increased operational complexity | Extract incrementally; automate deployment, observability, and local integration tests |
| Duplicate or lost work | Idempotent consumers, outbox, retries, and dead-letter monitoring |
| Inconsistent data across services | Explicit ownership, versioned events, and reconciliation |
| Search behavior changes | Preserve HTTP contracts and compare results during gradual traffic migration |
| Excessive cloud cost | Validate service tiers and usage assumptions before provisioning |
| Provider behavior differs between local and Azure | Keep provider adapters behind stable interfaces; test mapping, ranking, and error behavior locally |
| Azure AI services are unavailable or misconfigured | Timeouts, health checks, bounded retries, and explicit degraded behavior |

## 10. Recommended first milestone

Implement one vertical slice: move vector indexing to a worker, publish indexing requests through Azure Service Bus, and keep local Qdrant/Ollama adapters for local tests. Define and implement Azure AI Search/Azure OpenAI adapters behind the same interfaces for the Azure runtime. Verify idempotent processing, retry/dead-letter behavior, local provider behavior, observability, and compatibility with the current API. Do not make the test suite depend on live Azure services.
