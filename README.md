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

### Avatar-change logging

Mr. Clean logs changes to a member's effective server avatar in the configured avatar-log channel. Server-specific avatar overrides take precedence over global avatars. Each log contains the new image as an uploaded attachment; no previous avatar is included.

The logger subscribes before the Discord connection starts and waits for a full guild-member gateway download before silently initializing its in-memory snapshots. Downloads run outside gateway event handlers so member chunks can be processed. Failed initialization is retried every 30 seconds or when the guild becomes available. It then checks gateway member updates and performs a full guild-member gateway refresh every six hours and after reconnects, preserving existing snapshots to detect changes. Discord does not provide a dependable gateway event for another member's global avatar changes to bots, and the `USER_UPDATE` event is not a complete feed for all guild members. As a result, reconciliation can discover some changes missed by gateway events, but cannot guarantee complete or immediate coverage.

## Voice activity logging

Voice logging observes activity only. It does not replace VC Roles, assign/remove roles, rename channels or change permissions. Existing role-change logging continues independently, including changes made by VC Roles. Avatar logging and moderation remain enabled as before.

The feature is disabled by default. Supply the **source voice channel IDs** before enabling it. The following example uses symbolic source IDs: replace every `SOURCE_...` value/key with its numeric Discord voice channel ID before using this configuration. The three destination IDs are real **text channel IDs**, never source voice IDs.

```json
"VoiceLogging": {
  "Enabled": false,
  "LogChannelId": 806802061693550652,
  "GeneralChannelId": "SOURCE_GENERAL_VOICE_ID",
  "ReconnectWindowSeconds": 120,
  "Channels": {
    "SOURCE_MUSIC_ID": "Music",
    "SOURCE_GARTIC_PHONE_ID": "Gartic Phone",
    "SOURCE_CODENAMES_ID": "Codenames",
    "SOURCE_OTHER_VOICE_CHAT_ID": "Voice chat"
  },
  "ChannelLogDestinations": {
    "SOURCE_GENERAL_VOICE_ID": 806802061693550652,
    "SOURCE_MUSIC_ID": 943558876706533477,
    "SOURCE_GARTIC_PHONE_ID": 1556935771049689119,
    "SOURCE_CODENAMES_ID": 1556935771049689119,
    "SOURCE_OTHER_VOICE_CHAT_ID": 806802061693550652
  }
}
```

Use the intended guild (`Discord__GuildId=683633975838769192` in the current local configuration). Development appsettings target a different guild; explicitly set the guild/environment when deploying. Existing bot-token and PostgreSQL connection settings are reused.

| Destination | Text channel ID | Source channels to configure |
| --- | --- | --- |
| Music log | `943558876706533477` | Each music voice channel, with its fixed label |
| Fun games log | `1556935771049689119` | Gartic Phone, Codenames and each other game-specific voice channel |
| General voice log | `806802061693550652` | General renameable Voice; other ordinary voice channels if desired |

`Channels` retains the existing ID-to-fixed-label configuration. `ChannelLogDestinations` adds optional destination overrides keyed by **source voice channel ID**; `LogChannelId` remains the fallback. The general channel is tracked automatically through `GeneralChannelId`. A routing override alone does not enable tracking for other channels: add them to `Channels` too. Duplicate the music/game entries for every additional source voice channel. No IDs for source voice channels have been supplied yet; do not enable the placeholder example.

General Voice always routes by its ID, including while named Among Us or Lethal Company. Initial join logs use its name when the event is captured; departures use its current name at departure observation, never a later delivery-time rename. Recovery departures use the name observed during reconciliation. If the channel is missing, the stored activity is the fallback. Other channels always use their fixed labels. Earlier join logs are never edited. Active sessions retain their original destination across reconnects and configuration changes; new sessions use the updated route. Bots and mute/deafen changes are ignored.

Equivalent Docker variables include `VoiceLogging__Enabled=true`, `VoiceLogging__LogChannelId=806802061693550652`, `VoiceLogging__GeneralChannelId=<source-id>`, `VoiceLogging__ReconnectWindowSeconds=120`, `VoiceLogging__Channels__<source-id>=Gartic Phone` and `VoiceLogging__ChannelLogDestinations__<source-id>=1556935771049689119`. Settings are loaded at startup. The bot needs View Channel access to tracked voice channels and View Channel, Send Messages and **Read Message History** in each destination. The existing gateway intents include Guild Voice States. No role-management permission is needed for this feature, and it never changes permissions itself.

Gateway handlers immediately copy observations and their receipt timestamps to an in-memory FIFO and return without database or REST work. A single processor applies observations in order, including same-member rapid events. Joins generate a short line; departures generate a separate line with activity, cumulative connected time, session start and interruption count. Rejoining the same channel within 120 seconds (inclusive) continues the session and posts the excluded interruption duration. For example, 60 seconds connected, 30 disconnected, then 60 connected totals 120 seconds. A switch closes the old activity before starting the new one. No messages are edited or member mentions pinged.

Session changes and their new messages commit atomically to PostgreSQL (`VoiceActivityStates` and `VoiceLogMessages`, via an additive migration applied at bot startup). Outbox IDs are allocated in observation order and persisted with the session document. A separate sender delivers the oldest row per destination and removes it only after successful delivery. Slow Discord calls never hold the event processor. Failed destinations retain their head message while other destinations can progress; shared API delays/rate limits can still delay delivery. The sender wakes after commits and retries failures approximately every second, subject to API/database latency. Ordering is guaranteed within each destination; different destinations and existing role logs have no combined delivery ordering. A switch across destinations may therefore be visible in different wall-clock order between channels.

Each message has a short `[vc:N]` reference, unique within this bot database. Before sending, the sender persists an attempt timestamp. On an uncertain retry (including restart after a send but before acknowledgement), it checks the latest 100 destination messages for the same bot-authored content/reference before resending. Read Message History is required; inability to check history leaves that destination queued. This is bounded duplicate suppression, not exactly-once delivery: a long outage or busy channel can push a previously sent message beyond the history window, allowing a duplicate. The reference also distinguishes separate rapid events with identical visible timestamps. Uncertain state commits are retried using the same prepared snapshot and outbox IDs, without applying the event again.

Confirmed connections are checkpointed every 10 seconds. After restart or gateway reconnection, persisted connections end at their last confirmed checkpoint with an explicit observation-gap note, then observed occupants are reconciled. A gap within the grace window continues the session; otherwise a new session starts. Unobserved time is excluded, so some real connected time before an outage may be omitted. Disconnected sessions expire after the grace window. Members already present when tracking begins start at observation time.

Run **one bot instance per database/guild**. Normal shutdown drains captured observations into PostgreSQL within the host shutdown deadline; unsent outbox rows remain for the next start. Captured observations awaiting a database commit are in memory: a process crash can lose that tail, and a sustained database outage can grow the queue. Restart recovery reports the resulting uncertainty instead of reconstructing unknown events. Keep voice logging enabled until pending messages have drained. No live Discord/PostgreSQL recovery exercise has been performed yet; validate joins, switches, reconnects, renamed Voice departures and history permissions during a controlled deployment.

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
