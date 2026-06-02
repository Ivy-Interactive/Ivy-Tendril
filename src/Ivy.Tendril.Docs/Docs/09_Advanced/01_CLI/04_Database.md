---
searchHints:
  - database
  - db
  - migrate
  - migration
  - schema
  - version
  - reset
  - sqlite
---

# Database

<Ingress>
Manage the local SQLite database that stores plan sync data, recommendations, and cost tracking.
</Ingress>

## Commands

#### db-version

```terminal
>tendril db-version
```

Prints the current schema version number.

#### db-migrate

```terminal
>tendril db-migrate
```

Applies any pending migrations to bring the database schema up to date. Safe to run repeatedly — already-applied migrations are skipped.

`tendril run` applies pending migrations automatically on startup, so manual use of this command is rarely needed.

#### db-reset

```terminal
>tendril db-reset [--force]
```

Wipes all data and recreates the schema from scratch. Use `--force` to skip the confirmation prompt.

<Callout type="Warning">
This permanently deletes all stored data (recommendations, sync state, cost history). Plan YAML files on disk are not affected.

</Callout>
