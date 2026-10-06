# Mr. Clean

A Discord moderation and housekeeping bot for the **Good Gartic** Discord server.

This is a maintained fork of [Hex's original Mr. Clean](https://github.com/good-gartic/mr-clean), originally created as an alternative to CleanChat.

This fork retains Mr. Clean's message filtering system while adding support for modern Discord, configurable message retention, webhook/component-based messages, and role-change logging and routing.

## Features

### Message filtering

Mr. Clean supports configurable regular-expression message filters.

Filters can:

- match message text
- match embed content
- match modern Discord component text
- be restricted by channel, user or role
- be enabled or disabled independently
- delete matching messages after a configurable delay
- repost matching messages to another channel

Filter configuration is stored in PostgreSQL and managed through Discord slash commands.

### Message retention

Retention rules provide conservative automatic cleanup of old bot or webhook messages.

A retention rule can specify:

- a Discord channel
- a bot author or webhook username
- a minimum message age
- an existing message filter
- whether messages with human replies must be preserved

The retention scanner can inspect expiry dates contained in messages, including conventional dates and Discord timestamps.

Messages are preserved when:

- the configured filter does not match
- an expiry cannot be determined
- the message has not yet expired
- the message has reactions
- a human has replied, when reply preservation is enabled

Retention rules can be dry-run before deletion.

An automatic background retention service periodically processes enabled rules.

### Modern Discord message support

The original bot has been updated to:

- Discord.Net 3.18
- Discord API v10

Message matching supports text found in:

- normal message content
- embeds
- embed fields
- nested Discord message components
- Components V2 text displays

This allows filters and retention rules to work with newer bots and webhooks that no longer place their visible text in ordinary Discord message content.

### Webhook support

Retention rules can target a webhook by username rather than requiring a fixed webhook ID.

This is useful for services whose webhook IDs may change while their webhook identity remains consistent.

### Role-change logging

Mr. Clean can directly log Discord member role changes without relying on a separate logging bot.

A default role-log channel is configured for ordinary role changes. Individual roles can then be routed to different channels.

For example:

```text
Moderator changes    -> #role-log
Listening            -> #activitynoise
Watching             -> #activitynoise
Other Games          -> #activitynoise
Playing Gartic.io    -> #activities
```

Both role additions and removals are logged.

This is particularly useful for automatically assigned activity roles: high-volume activity changes can be separated from meaningful administrative role changes without losing the useful activity history.

If a role has no specific route, its changes go to the default role-log channel.

## Slash commands

### Filters

```text
/filter-allow
/create-filter
/edit-filter
/delete-filter
/filter-deny
/disable-filter
/enable-filter
/list-filters
/filter-reset
```

### Retention

```text
/create-retention
/edit-retention
/delete-retention
/disable-retention
/enable-retention
/list-retention
/test-retention
/run-retention
```

`/test-retention` performs a dry run.

`/run-retention` requires explicit confirmation before deleting eligible messages.

### Role logging

```text
/configure-role-logging
/role-log-route
/remove-role-log-route
/role-logging-settings
/set-role-logging
```

Role logging is disabled until explicitly enabled.

## Permissions

Mr. Clean's administrative slash commands require the Discord **Manage Server** (`ManageGuild`) permission.

The bot requires the Discord permissions needed to read message history, delete messages, send messages, and inspect member and role changes for the features being used.

The **Server Members Intent** is required for role-change logging.

## Database

Mr. Clean uses PostgreSQL through Entity Framework Core.

Database migrations are automatically applied when the bot starts.

The database stores:

- message filters
- retention rules
- role-logging configuration
- per-role log routes

## Building with Docker

Build the bot from the repository root:

```bash
docker build -t mr-clean:dev -f MrClean/Dockerfile .
```

A PostgreSQL instance must be available to the bot.

Example development run:

```bash
docker run --rm \
  --name mr-clean-dev \
  --network mr-clean-net \
  --env-file .env \
  -e DOTNET_ENVIRONMENT=Development \
  mr-clean:dev
```

## Configuration

Configuration can be supplied through an ASP.NET configuration file or environment variables.

The principal settings are:

```text
Discord__Token
Discord__GuildId
ConnectionStrings__Default
```

For example, an `.env` file used with Docker might contain:

```text
Discord__Token=YOUR_BOT_TOKEN
Discord__GuildId=YOUR_GUILD_ID
ConnectionStrings__Default=YOUR_POSTGRES_CONNECTION_STRING
```

Do not commit bot tokens, database passwords, or production `.env` files to Git.

## Running without Docker

Install the .NET 6 SDK and configure the required Discord and database settings.

The bot can then be run with:

```bash
dotnet run --project MrClean
```

For a production configuration file, copy `appsettings.json` to `appsettings.Production.json`, configure the required values, and run with the Production environment.

## Development

The project currently targets **.NET 6**.

Run the test suite with:

```bash
dotnet test
```

Database schema changes should be made through Entity Framework migrations rather than manually modifying the PostgreSQL schema.

## Retention safety

Deletion features are deliberately conservative.

The recommended workflow is:

1. Create and inspect the message filter.
2. Create the retention rule.
3. Run `/test-retention`.
4. Inspect the dry-run results.
5. Only then use `/run-retention` with confirmation.

Filters used solely as retention matchers can remain disabled.

Enabling a normal message filter may cause it to act on new matching messages independently of the retention system.

## Credits

Mr. Clean was originally written by **Hex** for Good Gartic.

Original project:

https://github.com/good-gartic/mr-clean

This fork updates and extends the original project for the current Good Gartic server and modern Discord API.

## License

See the repository's existing license for the licensing terms inherited from the original project.

![Mr. Clean logo](./logo.png)