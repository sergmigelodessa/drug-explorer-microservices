# DrugExplorer Microservices Migration — Quick Reference

## Related documents

- [Migration plan](microservices-architecture-plan.md) — proposed boundaries, Azure Service Bus design, phased extraction, and acceptance criteria.
- [Architecture diagrams](microservices-architecture-diagrams.md) — system overview, data ownership, event flows, and deployment view.
- [Azure Service Bus guide](microservices-azure-service-bus-guide.md) — illustrative publisher, consumer, configuration, and CLI examples.

## Suggested service boundaries

1. **API Gateway** — preserve the existing public API and route requests.
2. **Drug Catalog and Search** — own drug records and OpenFDA ingestion.
3. **Knowledge Indexing Worker** — process indexing and seeding work asynchronously.
4. **Semantic Search** — embed queries and retrieve matching chunks through the configured vector-store adapter.
5. **RAG Answering** — retrieve context, generate answers, and return source attribution.
6. **Analytics** — build read models from integration events.

Extract services incrementally. Avoid shared table access and do not split components without a clear ownership or scaling need.

## AI and vector providers

| Runtime | Embedding/chat provider | Vector provider |
|---|---|---|
| Local development and tests | Ollama | Qdrant |
| Azure deployment | Azure OpenAI Service | Azure AI Search |

Keep both provider sets behind application interfaces. Qdrant and Ollama are local-only; do not deploy them to Azure. Keep tests local and do not require live Azure services.

## Azure Service Bus

- Use queues for competing workers and topics with subscriptions when several consumers need the same event.
- Keep user-facing search and RAG request/response flows synchronous over HTTP.
- Start with events such as `DrugIngested`, `BuildIndexRequested`, `SearchCompleted`, and `RagAnswerCompleted`.
- Include stable message IDs, event type/schema version, UTC timestamp, and correlation ID.
- Make consumers idempotent; use bounded retries, exponential backoff, dead-letter monitoring, and an outbox for reliable event publication.
- Prefer managed identity and least-privilege RBAC; do not commit connection strings or secrets.

## Migration milestones

| Milestone | Main outcome |
|---|---|
| Baseline and contracts | Document existing behavior, ownership, telemetry, and event schemas |
| Async indexing | Move indexing to a worker and verify retries, idempotency, and dead-letter handling |
| Drug Catalog and Search | Extract persistence ownership while preserving HTTP behavior |
| Semantic Search and RAG | Extract only where independent deployment or scaling is justified |
| Analytics and retirement | Build event-fed projections, shift traffic gradually, and retire old paths safely |

## Operational checklist

- [ ] Define service and data ownership.
- [ ] Keep API and event contracts versioned.
- [ ] Provision Azure resources with Bicep or Terraform.
- [ ] Configure managed identities, network restrictions, and secret storage.
- [ ] Monitor latency, errors, queue age, retries, dead letters, and indexing outcomes.
- [ ] Test duplicate delivery, dependency outages, poison messages, replay, and rollback.
- [ ] Confirm Azure quotas and costs before deploying production workloads.

## First recommended slice

Move vector indexing to a worker, submit indexing requests through Azure Service Bus, and verify reliable processing and compatibility with the current API before extracting another capability.
