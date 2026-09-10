# Partner Integration BFF

Takes transaction notifications from partners over HTTP, checks the partner against a
downstream verification API, and puts accepted transactions on RabbitMQ.

```
partner ──HTTP──> Bff.Api ──HTTP──> Verification.Mock
                     │
                     └──AMQP──> RabbitMQ
```

## Architectural choices

- **Structure:** One BFF API project, organized by responsibility. Interfaces
  (`IPartnerVerificationClient`, `ITransactionPublisher`, `ICurrencyCatalog`) isolate external
  dependencies and keep unit tests independent of a network or broker. This keeps the exercise
  small while leaving clear extension points.
- **Verification mock:** A separate service in the same solution exercises real HTTP calls
  during local runs. Unit tests use a stub HTTP transport to exercise the production resilience
  pipeline deterministically.
- **Response contract:** `202 Accepted` indicates queued work awaiting downstream processing;
  the response echoes `transactionReference` for correlation. Verification uses three outcomes:
  `Verified` proceeds to publishing, `NotVerified` returns `422`, and `Unavailable` returns `503`,
  keeping business rejection distinct from a temporary dependency failure.
- **Resilience:** verification in a 3s total timeout, up to three retries with
  exponential backoff and jitter, and a 1s timeout per attempt. The total budget includes retries
  and delays. A circuit breaker is omitted to keep this exercise focused on bounded retries;
  production thresholds would need to account for expected failures and traffic.
- **Reliable messaging:** Durable RabbitMQ topology, persistent messages, and publisher confirms
  ensure `202` is returned only after broker acknowledgement. Mandatory publishing surfaces
  unroutable messages; publish failures return `500`. The message ID is
  `{partnerId}:{transactionReference}` — partner-scoped, because a reference is only unique
  within one partner — enabling consumer-side deduplication when retries produce duplicates.

## Running the project

```sh
docker compose up --build
```

| | |
|---|---|
| API | http://localhost:8080 |
| Verification mock | http://localhost:8081 |
| RabbitMQ UI | http://localhost:15672 (guest/guest) |

```sh
curl -i -X POST http://localhost:8080/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -d '{
        "partnerId": "P-1001",
        "transactionReference": "TXN-99823",
        "amount": 250.00,
        "currency": "USD",
        "timestamp": "2026-05-10T14:30:00Z"
      }'
```

You get `202 Accepted` back and the message shows up in the RabbitMQ UI under
`partner.transactions.accepted`, or:

```sh
docker exec shop-rabbitmq rabbitmqctl list_queues name messages
```

The verification mock fails about 30% of calls on purpose, so you'll sometimes get a 503
instead. That's the retry pipeline giving up and reporting the downstream as unavailable.
Send it again.

## Running the tests

```sh
dotnet test
```

62 tests, about a second, no Docker needed.

Coverage is collected automatically via `coverlet.runsettings`. For an HTML report:

```sh
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:Html
```
