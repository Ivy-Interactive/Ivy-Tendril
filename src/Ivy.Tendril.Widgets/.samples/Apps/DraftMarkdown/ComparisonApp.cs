using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.DraftMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

[App(icon: Icons.FolderSync, group: ["DraftMarkdown"])]
class ComparisonApp : ViewBase
{
    public override object Build()
    {
        var markdown = """
            # Plan: Migrate to Event-Driven Architecture

            ## Overview
            This plan migrates the order processing pipeline from synchronous HTTP calls
            to an event-driven architecture using message queues. See [Plan #01205](plan://01205)
            for the prerequisite infrastructure setup.

            ## Architecture

            ```mermaid
            flowchart LR
                A[API Gateway] --> B[Order Service]
                B --> C{Valid?}
                C -->|Yes| D[Event Bus]
                C -->|No| E[Error Queue]
                D --> F[Payment Service]
                D --> G[Inventory Service]
                D --> H[Notification Service]
                F --> I[Saga Orchestrator]
                G --> I
                I --> J[Order Complete]
            ```

            ## Service Dependencies

            ```dot
            digraph G {
                rankdir=TB;
                node [shape=box, style=rounded];
                OrderAPI [label="Order API"];
                PaymentSvc [label="Payment Service"];
                InventorySvc [label="Inventory Service"];
                NotifySvc [label="Notification Service"];
                EventBus [label="Event Bus", shape=ellipse];
                DB [label="Order DB", shape=cylinder];

                OrderAPI -> EventBus [label="publish"];
                EventBus -> PaymentSvc [label="subscribe"];
                EventBus -> InventorySvc [label="subscribe"];
                EventBus -> NotifySvc [label="subscribe"];
                OrderAPI -> DB [label="write"];
                PaymentSvc -> EventBus [label="ack/nack"];
            }
            ```

            ## Sequence: Order Placement

            ```mermaid
            sequenceDiagram
                participant Client
                participant API as Order API
                participant Bus as Event Bus
                participant Pay as Payment
                participant Inv as Inventory

                Client->>API: POST /orders
                API->>API: Validate & persist
                API-->>Client: 202 Accepted
                API->>Bus: OrderCreated event
                Bus->>Pay: Process payment
                Bus->>Inv: Reserve stock
                Pay-->>Bus: PaymentConfirmed
                Inv-->>Bus: StockReserved
                Bus->>API: SagaComplete
            ```

            ## Implementation Steps

            | Step | Status | Owner | Notes |
            |------|--------|-------|-------|
            | Define event schemas | Done | @alice | Protobuf v3 |
            | Provision message broker | Done | @infra | RabbitMQ cluster |
            | Order service publisher | In progress | @bob | 80% complete |
            | Payment consumer | Pending | @carol | Blocked on schema |
            | Inventory consumer | Pending | @dave | — |
            | Saga orchestrator | Pending | TBD | Needs design review |

            ## Verification

            - [x] Event schemas validated against production data
            - [x] Broker failover tested (3-node cluster)
            - [ ] End-to-end integration test
            - [ ] Load test at 2x peak traffic
            - [ ] Rollback procedure documented

            ## Configuration

            ```yaml
            services:
              order-api:
                publish:
                  - exchange: orders
                    routing_key: order.created
                retry:
                  max_attempts: 3
                  backoff: exponential

              payment-service:
                subscribe:
                  - queue: payments.process
                    binding: order.created
                dead_letter:
                  exchange: payments.dlx
                  ttl: 86400
            ```

            ## Rollback Strategy

            ```csharp
            public class OrderSagaOrchestrator
            {
                public async Task CompensateAsync(OrderSagaContext ctx)
                {
                    if (ctx.PaymentConfirmed)
                        await _payments.RefundAsync(ctx.PaymentId);

                    if (ctx.StockReserved)
                        await _inventory.ReleaseAsync(ctx.ReservationId);

                    await _orders.MarkFailedAsync(ctx.OrderId, ctx.FailureReason);
                }
            }
            ```

            > [!NOTE]
            > The saga orchestrator uses at-least-once delivery semantics. All consumers
            > must be idempotent — use the `EventId` for deduplication.
            """;

        return Layout.Horizontal().Height(Size.Full()).Gap(4)
               | (Layout.Vertical().Width(Size.Fraction(0.5f)).Height(Size.Full())
                  | Text.Block("Markdown (Framework)").Bold().Small()
                  | new Markdown(markdown).Article().Height(Size.Full()))
               | (Layout.Vertical().Width(Size.Fraction(0.5f)).Height(Size.Full())
                  | Text.Block("DraftMarkdown (Widget)").Bold().Small()
                  | new DraftMarkdownWidget(markdown).Article().Height(Size.Full()));
    }
}
