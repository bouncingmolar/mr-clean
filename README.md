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

Voice logging observes activity only. It does not replace VC Roles, assign/remove roles, rename channels or change permissions. Existing role-change logging continues for all other roles. While voice logging is enabled, additions/removals of the configured Connected role are omitted to avoid duplicate activity announcements. VC Roles assignments themselves are unchanged. Avatar logging and moderation remain enabled as before.

The server mapping below is configured in `MrClean/appsettings.json`. Voice logging remains **disabled**; enabling it for a controlled test is a separate deployment step. All IDs are public configuration, and bot tokens/database credentials remain outside Git.

```json
"VoiceLogging": {
  "Enabled": false,
  "LogChannelId": 806802061693550652,
  "GeneralChannelId": 798528429091061780,
  "ReconnectWindowSeconds": 120,
  "ConnectedRoleId": 894787914599370803,
  "Channels": {
    "792285032713945119": "Music",
    "1126920146099912744": "Gartic Phone",
    "1126902663607484476": "Codenames"
  },
  "ChannelLogDestinations": {
    "798528429091061780": 806802061693550652,
    "792285032713945119": 943558876706533477,
    "1126920146099912744": 1556935771049689119,
    "1126902663607484476": 1556935771049689119
  }
}
```

Use `Discord__GuildId=683633975838769192`. Development appsettings target a different guild; explicitly set the intended guild/environment when deploying. Existing bot-token and PostgreSQL connection settings are reused.

| Source voice channel ID | Activity label | Destination text channel ID |
| --- | --- | --- |
| `798528429091061780` | Current channel name on departure | `806802061693550652` |
| `792285032713945119` | Music | `943558876706533477` |
| `1126920146099912744` | Gartic Phone | `1556935771049689119` |
| `1126902663607484476` | Codenames | `1556935771049689119` |

`Channels` retains the ID-to-fixed-label configuration. `ChannelLogDestinations` adds destination overrides keyed by **source voice channel ID**; `LogChannelId` remains the fallback. The general channel is tracked automatically through `GeneralChannelId`. A routing override alone does not enable tracking for other channels: add them to `Channels` too.

General Voice always routes by its ID, including while named Among Us or Lethal Company. Join logs use actual user/channel mentions. Departure descriptions include the activity label in parentheses beside the channel mention: the general channel uses its current name at departure observation, never a later delivery-time rename. Recovery departures use the name observed during reconciliation. If the channel is missing, the stored activity is the fallback. Other channels always use their fixed labels. Earlier join logs are never edited. Active sessions retain their original destination across reconnects and configuration changes; new sessions use the updated route. Bots and mute/deafen changes are ignored.

Equivalent Docker variables include `VoiceLogging__Enabled=true`, `VoiceLogging__LogChannelId=806802061693550652`, `VoiceLogging__GeneralChannelId=<source-id>`, `VoiceLogging__ReconnectWindowSeconds=120`, `VoiceLogging__Channels__<source-id>=Gartic Phone`, `VoiceLogging__ConnectedRoleId=894787914599370803` and `VoiceLogging__ChannelLogDestinations__<source-id>=1556935771049689119`. Settings are loaded at startup. The bot needs View Channel access to tracked voice channels and View Channel, Send Messages, **Embed Links** and **Read Message History** in each destination. The existing gateway intents include Guild Voice States. No role-management permission is needed for this feature, and it never changes permissions itself.

Gateway handlers immediately copy observations and their receipt timestamps to an in-memory FIFO and return without database or REST work. A single processor applies observations in order, including same-member rapid events. Joins and reconnects send exactly one plain-text line with real Discord mentions and no embed, e.g. `🔊 <@user-id> joined <#voice-channel-id>`. No Connected-role announcement accompanies a join.

Departures send **only an embed**, with no external message content. Its description contains three lines, for example:

```text
@dr.tim left #music (Music)
Connected for 12 minutes.
0 brief interruptions.
```

The first line contains real user/channel mentions and the recorded activity label, preserving fixed labels even if their channels are renamed and the general channel's departure-time name if it is renamed again later. There are no fields, title, author, footer, visible log references or timestamps. The interruption count always appears, including zero. Durations use readable hours/minutes/seconds and exclude disconnected time. Recovery adds one short observation-gap sentence so confirmed time is not presented as complete history.

Rejoining the same channel within 120 seconds (inclusive) continues the session but uses the same short join format; interruption counts appear on later departures. For example, 60 seconds connected, 30 disconnected, then 60 connected totals 120 seconds. A switch generates the old departure before the new join. No messages are edited or member mentions pinged.

`VoiceLogging:ConnectedRoleId` is `894787914599370803`, the existing `connected` role verified through a read-only Discord lookup. Filtering compares IDs, not role names, and applies to both additions and removals only in the configured guild while voice logging is enabled. Zero/unset leaves all role changes logged. Mixed role changes retain every other role and its existing routing. This affects logging only, never assignments or permissions.

Session changes and their new messages commit atomically to PostgreSQL (`VoiceActivityStates` and `VoiceLogMessages`, via an additive migration applied at bot startup). Outbox IDs are allocated in observation order and persisted with the session document. A separate sender delivers the oldest row per destination and removes it only after successful delivery. Slow Discord calls never hold the event processor. Failed destinations retain their head message while other destinations can progress; shared API delays/rate limits can still delay delivery. The sender wakes after commits and retries failures approximately every second, subject to API/database latency. Ordering is guaranteed within each destination; different destinations and existing role logs have no combined delivery ordering. A switch across destinations may therefore be visible in different wall-clock order between channels.

Retry metadata is internal to PostgreSQL; new messages have no visible reference. Before a first send, the sender reads the latest destination message ID and persists that history baseline and attempt timestamp with a versioned payload in the existing outbox text column. On an uncertain retry it checks the latest 100 bot-authored messages newer than that baseline for matching content/embed description. This distinguishes prior identical rapid events without adding footers or timestamps. Read Message History is required on first attempts too. Delivery remains at least once: a busy channel or long outage can push a sent message beyond the bounded history window and allow a duplicate. Uncertain state commits reuse their prepared snapshot and IDs without applying the observation twice.

No database migration is required for this format change. Already queued legacy text messages are delivered verbatim, preserving their identity for retry detection; newly generated messages use the final format. Older voice-logging binaries cannot read new versioned payloads: before rolling back to such a binary, drain the new queue or disable voice logging in the rollback runtime configuration and preserve queued rows. The older pre-voice logger ignores voice tables entirely.

Confirmed connections are checkpointed every 10 seconds. After restart or gateway reconnection, persisted connections end at their last confirmed checkpoint with an explicit observation-gap note, then observed occupants are reconciled. A gap within the grace window continues the session; otherwise a new session starts. Unobserved time is excluded, so some real connected time before an outage may be omitted. Disconnected sessions expire after the grace window. Members already present when tracking begins start at observation time.

Run **one bot instance per database/guild**. Normal shutdown drains captured observations into PostgreSQL within the host shutdown deadline; unsent outbox rows remain for the next start. Captured observations awaiting a database commit are in memory: a process crash can lose that tail, and a sustained database outage can grow the queue. Restart recovery reports the resulting uncertainty instead of reconstructing unknown events. Keep voice logging enabled until pending messages have drained. No live Discord/PostgreSQL recovery exercise has been performed yet; validate joins, switches, reconnects, renamed Voice departures and history permissions during a controlled deployment.

Deployment preparation, the controlled test and rollback are described in [the deployment plan](docs/voice-logging-deployment.md).

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
