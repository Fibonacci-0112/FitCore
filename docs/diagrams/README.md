# Diagrams

Excalidraw diagrams of the accepted architecture in [ADR 0003](../decisions/0003-modular-monolith.md). They describe the design, not built features: the solution is still a scaffold.

| File | Shows |
|---|---|
| [`architecture.excalidraw`](architecture.excalidraw) | Clients, the two process types on ECS Fargate, `FitCore.Platform`, the 12 modules and the data platform (Supabase Postgres, Auth, Storage, Valkey) |
| [`target-workflow.excalidraw`](target-workflow.excalidraw) | A coach sets a member's target: JWT, rate limit, live grant, hard floor and soft limit (BRD §6.2), `SET LOCAL` and the RLS re-check, then the audit row, outbox and notification |
| [`data-flow.excalidraw`](data-flow.excalidraw) | An offline food log syncing to the Nutrition schema, an adaptive-target proposal from Progress behind the data-quality gate (BRD §6.2) that Targets applies only after the member accepts, and a coach read under a live grant with the 24-hour cache lease (NFR-PRV-4) |

Open a file at [excalidraw.com](https://excalidraw.com) (Open, or drag the file in), in Excalidraw+, or with the Excalidraw extension for VS Code. Save edits back to the same file and update this table when a diagram changes.

When the BRD, the NFRs or an ADR changes something a diagram shows, update the diagram in the same PR.
