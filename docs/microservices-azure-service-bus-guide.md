# DrugExplorer Microservices - Azure Service Bus Implementation Guide

## Provider profile

The local development and test profile uses Qdrant and Ollama. They are local-only and must not be deployed to Azure. The Azure runtime target is Azure AI Search for vector search/indexing and Azure OpenAI Service for embeddings and chat. Implement both provider sets behind the existing application interfaces and select them through environment-specific dependency injection.

Run tests locally only. Use local Qdrant/Ollama for local provider integration tests and local fakes/mocks for Azure adapter tests; the test suite must not require live Azure services. Azure adapter names in examples below describe planned implementations, not existing classes.

## Part 1: Shared Interfaces and Models

### 1.1 Event Publisher Interface

```
// DrugExplorer.Shared/Events/IEventPublisher.cs

using System;
using System.Threading.Tasks;

namespace DrugExplorer.Shared.Events;

public interface IEventPublisher
{
    Task PublishAsync<T>(ServiceBusEvent<T> @event, string topicName) where T : class;
    Task PublishAsync(string message, string topicName);
}

public class ServiceBusEvent<T> where T : class
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; }
    public T Data { get; set; }
}
```

### 1.2 Event Listener Interface

```
// DrugExplorer.Shared/Events/IEventListener.cs

using System;
using System.Threading.Tasks;

namespace DrugExplorer.Shared.Events;

public interface IEventListener
{
    Task HandleEventAsync(string eventType, string messageBody);
}

public interface IEventHandler<T> where T : class
{
    string EventType { get; }
    Task HandleAsync(ServiceBusEvent<T> @event);
}
```

---

## Part 2: Azure Service Bus Publisher and Listener

### 2.1 Service Bus Event Publisher

```
// DrugExplorer.Shared/Events/ServiceBusEventPublisher.cs

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace DrugExplorer.Shared.Events;

public class ServiceBusEventPublisher : IEventPublisher
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<ServiceBusEventPublisher> _logger;

    public ServiceBusEventPublisher(
        ServiceBusClient serviceBusClient,
        ILogger<ServiceBusEventPublisher> logger)
    {
        _serviceBusClient = serviceBusClient;
        _logger = logger;
    }

    public async Task PublishAsync<T>(ServiceBusEvent<T> @event, string topicName) where T : class
    {
        try
        {
            var sender = _serviceBusClient.CreateSender(topicName);
            var messageBody = JsonSerializer.Serialize(@event);
            var message = new ServiceBusMessage(messageBody)
            {
                ContentType = "application/json",
                CorrelationId = @event.CorrelationId,
                MessageId = @event.EventId,
                Subject = @event.EventType,
                TimeToLive = TimeSpan.FromMinutes(5)
            };

            await sender.SendMessageAsync(message);

            _logger.LogInformation(
                "Event published: {EventType}, EventId: {EventId}, Topic: {Topic}",
                @event.EventType, @event.EventId, topicName);

            await sender.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish event {EventType} to topic {Topic}",
                @event.EventType, topicName);
            throw;
        }
    }

    public async Task PublishAsync(string message, string topicName)
    {
        try
        {
            var sender = _serviceBusClient.CreateSender(topicName);
            var sbMessage = new ServiceBusMessage(message)
            {
                ContentType = "application/json"
            };

            await sender.SendMessageAsync(sbMessage);
            await sender.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to topic {Topic}", topicName);
            throw;
        }
    }
}
```

### 2.2 Service Bus Event Listener

```
// DrugExplorer.Shared/Events/ServiceBusEventListener.cs

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace DrugExplorer.Shared.Events;

public class ServiceBusEventListener : IEventListener
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<ServiceBusEventListener> _logger;
    private readonly Dictionary<string, Func<string, Task>> _handlers = new();

    public ServiceBusEventListener(
        ServiceBusClient serviceBusClient,
        ILogger<ServiceBusEventListener> logger)
    {
        _serviceBusClient = serviceBusClient;
        _logger = logger;
    }

    public void RegisterHandler(string eventType, Func<string, Task> handler)
    {
        _handlers[eventType] = handler;
    }

    public async Task HandleEventAsync(string eventType, string messageBody)
    {
        try
        {
            if (_handlers.TryGetValue(eventType, out var handler))
            {
                await handler(messageBody);
                _logger.LogInformation("Event handled: {EventType}", eventType);
            }
            else
            {
                _logger.LogWarning("No handler registered for event type: {EventType}", eventType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event: {EventType}", eventType);
            throw;
        }
    }
}
```

### 2.3 Service Bus Consumer Hosted Service

```
// DrugExplorer.Shared/Events/ServiceBusConsumerHostedService.cs

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DrugExplorer.Shared.Events;

public class ServiceBusConsumerHostedService : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IEventListener _eventListener;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private readonly ILogger<ServiceBusConsumerHostedService> _logger;
    private ServiceBusProcessor _processor;

    public ServiceBusConsumerHostedService(
        ServiceBusClient serviceBusClient,
        IEventListener eventListener,
        string topicName,
        string subscriptionName,
        ILogger<ServiceBusConsumerHostedService> logger)
    {
        _serviceBusClient = serviceBusClient;
        _eventListener = eventListener;
        _topicName = topicName;
        _subscriptionName = subscriptionName;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var options = new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 1,
                AutoCompleteMessages = false,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            };

            _processor = _serviceBusClient.CreateProcessor(
                _topicName, _subscriptionName, options);

            _processor.ProcessMessageAsync += MessageHandler;
            _processor.ProcessErrorAsync += ErrorHandler;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation(
                "Service Bus consumer started for topic {TopicName}, subscription {SubscriptionName}",
                _topicName, _subscriptionName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service Bus consumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Service Bus consumer");
            throw;
        }
    }

    private async Task MessageHandler(ProcessMessageEventArgs args)
    {
        try
        {
            var messageBody = args.Message.Body.ToString();
            var eventType = args.Message.Subject ?? "Unknown";

            _logger.LogInformation("Processing message: {EventType}, MessageId: {MessageId}",
                eventType, args.Message.MessageId);

            await _eventListener.HandleEventAsync(eventType, messageBody);
            await args.CompleteMessageAsync(args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {MessageId}", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "ProcessingError", ex.Message);
        }
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Error in Service Bus processor. Exception: {Exception}, Action: {Action}",
            args.Exception.Message, args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
```

---

## Part 3: Drug Search Service Example

### 3.1 Drug Search Event

```
// DrugExplorer.DrugSearchService/Events/DrugSearchEvent.cs

namespace DrugExplorer.DrugSearchService.Events;

public class DrugSearchInitiatedEvent
{
    public string Query { get; set; }
    public string CacheKey { get; set; }
    public DateTime Timestamp { get; set; }
}

public class DrugsFoundEvent
{
    public string Query { get; set; }
    public int ResultCount { get; set; }
    public long ResponseTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 3.2 Updated Drug Search Service

```
// DrugExplorer.DrugSearchService/Services/DrugSearchService.cs

using DrugExplorer.Shared.Events;
using DrugExplorer.DrugSearchService.Events;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DrugExplorer.DrugSearchService.Services;

public class DrugSearchService : IDrugSearchService
{
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DrugSearchService> _logger;
    private readonly IDrugRepository _drugRepository;
    // ... other dependencies

    public DrugSearchService(
        IEventPublisher eventPublisher,
        ILogger<DrugSearchService> logger,
        IDrugRepository drugRepository)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
        _drugRepository = drugRepository;
    }

    public async Task<DrugSearchResult> SearchAsync(string query)
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            // Search OpenFDA and cache the results
            var result = await PerformSearchAsync(query);

            var elapsed = DateTime.UtcNow - startTime;

            // Publish an indexing event
            var searchEvent = new ServiceBusEvent<DrugSearchInitiatedEvent>
            {
                EventType = "drug-search-initiated",
                CorrelationId = correlationId,
                Data = new DrugSearchInitiatedEvent
                {
                    Query = query,
                    CacheKey = $"search_{query.ToLower()}",
                    Timestamp = startTime
                }
            };

            await _eventPublisher.PublishAsync(searchEvent, "drug-search-events");

            // Publish an analytics event
            var foundEvent = new ServiceBusEvent<DrugsFoundEvent>
            {
                EventType = "drugs-found",
                CorrelationId = correlationId,
                Data = new DrugsFoundEvent
                {
                    Query = query,
                    ResultCount = result.Count,
                    ResponseTimeMs = (long)elapsed.TotalMilliseconds,
                    Timestamp = startTime
                }
            };

            await _eventPublisher.PublishAsync(foundEvent, "drug-search-events");

            _logger.LogInformation(
                "Search completed for query '{Query}', found {Count} results, took {Elapsed}ms",
                query, result.Count, elapsed.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query '{Query}'", query);
            throw;
        }
    }

    private async Task<DrugSearchResult> PerformSearchAsync(string query)
    {
        // Search implementation
        return new DrugSearchResult { };
    }
}
```

### 3.3 Drug Search Service Program.cs

```
// DrugExplorer.DrugSearchService/Program.cs

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using DrugExplorer.Shared.Events;
using DrugExplorer.DrugSearchService.Services;
using DrugExplorer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// Azure clients
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddServiceBusClient(
        builder.Configuration.GetConnectionString("ServiceBusConnection"))
        .WithName("ServiceBusClient");
});

// Database
builder.Services.AddDbContext<DrugExplorerDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Application services
builder.Services.AddScoped<IDrugSearchService, DrugSearchService>();
builder.Services.AddScoped<IEventPublisher, ServiceBusEventPublisher>();

// Controllers
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();

var app = builder.Build();

app.UseSwagger();
app.UseRouting();
app.MapControllers();

app.Run();
```

---

## Part 4: Vector Index Builder Service Example

### 4.1 Vector Builder Event Handler

```
// DrugExplorer.VectorBuilderService/Events/DrugSearchEventHandler.cs

using DrugExplorer.Shared.Events;
using DrugExplorer.DrugSearchService.Events;
using DrugExplorer.VectorBuilderService.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace DrugExplorer.VectorBuilderService.Events;

public class DrugSearchEventHandler : IEventHandler<DrugSearchInitiatedEvent>
{
    private readonly IVectorIndexBuildService _buildService;
    private readonly ILogger<DrugSearchEventHandler> _logger;

    public string EventType => "drug-search-initiated";

    public DrugSearchEventHandler(
        IVectorIndexBuildService buildService,
        ILogger<DrugSearchEventHandler> logger)
    {
        _buildService = buildService;
        _logger = logger;
    }

    public async Task HandleAsync(ServiceBusEvent<DrugSearchInitiatedEvent> @event)
    {
        try
        {
            _logger.LogInformation(
                "Starting vector index build for query: {Query}, CorrelationId: {CorrelationId}",
                @event.Data.Query, @event.CorrelationId);

            // Start the index build asynchronously
            await _buildService.BuildAsync(CancellationToken.None);

            _logger.LogInformation("Vector index build completed for query: {Query}", @event.Data.Query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error building vector index for query: {Query}",
                @event.Data.Query);
            throw;
        }
    }
}
```

### 4.2 Vector Builder Service Program.cs

```
// DrugExplorer.VectorBuilderService/Program.cs

using Azure.Messaging.ServiceBus;
using DrugExplorer.Shared.Events;
using DrugExplorer.VectorBuilderService.Events;
using DrugExplorer.VectorBuilderService.Services;
using DrugExplorer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Azure clients
var serviceBusClient = new ServiceBusClient(
    builder.Configuration.GetConnectionString("ServiceBusConnection"));
builder.Services.AddSingleton(serviceBusClient);

// Database
builder.Services.AddDbContext<DrugExplorerDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Event handling
builder.Services.AddScoped<IEventPublisher, ServiceBusEventPublisher>();
builder.Services.AddScoped<IEventListener, ServiceBusEventListener>();
builder.Services.AddScoped<IEventHandler<DrugSearchInitiatedEvent>, DrugSearchEventHandler>();

// Application services
builder.Services.AddScoped<IVectorIndexBuildService, VectorIndexBuildService>();
// Azure runtime registrations (planned adapters):
builder.Services.AddScoped<IVectorStore, AzureAiSearchVectorStore>();
builder.Services.AddScoped<IVectorIndexWriter, AzureAiSearchVectorStore>();
builder.Services.AddScoped<IEmbeddingService, AzureOpenAiEmbeddingClient>();
builder.Services.AddScoped<IAIModelClientService, AzureOpenAiChatClient>();

// Hosted service for processing messages
builder.Services.AddSingleton(sp =>
{
    var eventListener = sp.GetRequiredService<IEventListener>();
    var handler = sp.GetRequiredService<IEventHandler<DrugSearchInitiatedEvent>>();
    var logger = sp.GetRequiredService<ILogger<ServiceBusConsumerHostedService>>();

    // Register handler in listener
    var listeners = eventListener as ServiceBusEventListener;
    listeners?.RegisterHandler("drug-search-initiated",
        async (messageBody) =>
        {
            var @event = System.Text.Json.JsonSerializer.Deserialize<
                ServiceBusEvent<DrugSearchInitiatedEvent>>(messageBody);
            await handler.HandleAsync(@event);
        });

    return new ServiceBusConsumerHostedService(
        serviceBusClient,
        eventListener,
        "drug-search-events",
        "vector-builder-subscription",
        logger);
});

builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<ServiceBusConsumerHostedService>());

builder.Services.AddControllers();
builder.Services.AddLogging();

var app = builder.Build();
app.MapControllers();
app.Run();
```

---

## Part 5: Azure Service Bus Configuration

### 5.1 appsettings.json

Keep non-secret routing and provider settings in configuration. In Azure, authenticate to Service Bus, SQL, Azure AI Search, and Azure OpenAI with managed identities where supported. Do not put credentials, keys, or passwords in committed configuration; use a local secret store for development.

```
{
  "Providers": {
    "VectorStore": "AzureAISearch",
    "Ai": "AzureOpenAI"
  },
  "AzureAiSearch": {
    "Endpoint": "https://<search-service>.search.windows.net",
    "IndexName": "drug-chunks"
  },
  "AzureOpenAI": {
    "Endpoint": "https://<openai-resource>.openai.azure.com",
    "EmbeddingDeployment": "<embedding-deployment>",
    "ChatDeployment": "<chat-deployment>"
  },
  "ServiceBus": {
    "Namespace": "<namespace>.servicebus.windows.net",
    "Topics": {
      "DrugSearch": "drug-search-events",
      "SemanticSearch": "semantic-search-events",
      "RagEvents": "rag-events",
      "SeedingEvents": "seeding-events",
      "VectorIndexEvents": "vector-index-events"
    },
    "Subscriptions": {
      "VectorBuilder": "vector-builder-subscription",
      "Analytics": "analytics-subscription"
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Azure.Messaging.ServiceBus": "Information"
    }
  }
}
```

### 5.2 Azure CLI Commands to Create Service Bus Resources

```
# Create Service Bus Namespace
az servicebus namespace create \
  --resource-group drugexplorer-rg \
  --name drugexplorer \
  --location eastus \
  --sku Premium

# Create Topics
az servicebus topic create \
  --resource-group drugexplorer-rg \
  --namespace-name drugexplorer \
  --name drug-search-events

az servicebus topic create \
  --resource-group drugexplorer-rg \
  --namespace-name drugexplorer \
  --name vector-index-events

# Create Subscriptions
az servicebus topic subscription create \
  --resource-group drugexplorer-rg \
  --namespace-name drugexplorer \
  --topic-name drug-search-events \
  --name vector-builder-subscription

az servicebus topic subscription create \
  --resource-group drugexplorer-rg \
  --namespace-name drugexplorer \
  --topic-name drug-search-events \
  --name analytics-subscription

# Get connection string
az servicebus namespace authorization-rule keys list \
  --resource-group drugexplorer-rg \
  --namespace-name drugexplorer \
  --name RootManageSharedAccessKey
```

---

## Conclusion

This document provides:
- Shared interfaces for event publishing and handling
- Azure Service Bus publisher and listener examples
- Examples for the Drug Search and Vector Builder services
- Configuration and Azure CLI commands for creating infrastructure

Use these examples as a starting point for other services in the system.