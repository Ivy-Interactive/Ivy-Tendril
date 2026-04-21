---
icon: Database
searchHints:
  - database
  - db
  - migrate
  - reset
  - version
  - schema
---

<Text Color="Green" Small Bold>CLI</Text>

# Database

<Ingress>
Manage the local SQLite database that stores plan sync data, recommendations, and cost tracking.
</Ingress>

## db-version

```bash
tendril db-version
```

Prints the current schema version number.

## db-migrate

```bash
tendril db-migrate
```

Applies any pending migrations to bring the database schema up to date. Safe to run repeatedly — already-applied migrations are skipped.

## db-reset

```bash
tendril db-reset [--force]
```

Wipes all data and recreates the schema from scratch.

| Option | Effect |
|--------|--------|
| `--force` | Skip the interactive confirmation prompt |

<Callout type="Warning">
This permanently deletes all stored data (recommendations, sync state, cost history). Plan files on disk are not affected.

</Callout>
