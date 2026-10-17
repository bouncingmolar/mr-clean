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

## Voice Channel Rules

Voice rules are stored in PostgreSQL and keyed by guild and source **voice channel ID**, not channel names. Runtime selection, display wording, destinations, logging switches, timing switches and activity roles come from rules. Each Mr-Clean instance still serves its existing configured `Discord__GuildId`; commands reject other guilds. Rules and role ownership are isolated by guild in the database.

The live `bee7e73` deployment is unchanged until an approved deployment. The new migration seeds these four enabled rules with both logs and both timing options enabled, no activity roles, and the existing `{activity}` wording:

| Source voice channel | Display | Destination text channel |
| --- | --- | --- |
| `798528429091061780` | Current channel name on departure | `806802061693550652` |
| `792285032713945119` | Music (fixed) | `943558876706533477` |
| `1126920146099912744` | Gartic Phone (fixed) | `1556935771049689119` |
| `1126902663607484476` | Codenames (fixed) | `1556935771049689119` |

The four source IDs exist only in migration seed data, never runtime selection logic. Old `GeneralChannelId`, `Channels` and `ChannelLogDestinations` configuration is no longer read. The global `VoiceLogging` options retain `Enabled`, `ReconnectWindowSeconds` (120), `ConnectedRoleId` (894787914599370803, protected from activity role management) and `LogChannelId` (fallback for legacy sessions). Existing production environment secrets, enabled state, guild ID and connection string remain unchanged.

### Administration

`/voice` commands require Manage Server (`ManageGuild`) or Administrator permission. Both the dispatcher and handler check permissions server-side, and replies are ephemeral. Add/edit use Discord's native voice/text channel pickers and role pickers. Roles can be added one per edit, allowing any number per rule.

```text
/voice add channel:#codenames destination:#fungis name:Codenames wording:"Playing {activity}"
/voice edit channel:#codenames join-log:false departure-log:true
/voice edit channel:#codenames add-role:@Playing-Codenames
/voice edit channel:#codenames remove-role:@Playing-Codenames
/voice edit channel:#codenames clear-roles:true
/voice edit channel:#music duration:true interruptions:true
/voice edit channel:#voice dynamic:true wording:"In {activity}"
/voice edit channel:#codenames enabled:false
/voice list
/voice remove channel:#codenames
/voice remove channel-id:1126902663607484476
```

The last form permits cleanup after a source channel has been deleted. An edit omits any fields that should stay unchanged. `dynamic:true` substitutes the observed current channel name for `{activity}`; otherwise it substitutes `name`. Wording may also be a fixed phrase such as `Playing Codenames`. Name/wording are single-line and limited to 100/150 characters. `/voice list` shows IDs, enabled state, fixed/dynamic naming, wording, both logging/timing switches, destination and all activity roles. Listing does not ping roles or members.

Voice activity-role grants are opt-in: **the four voice rules have empty role lists**. Playing Codenames must be explicitly configured later. Role changes reconcile promptly after voice/presence events and rule edits, and every thirty seconds by default against actual cached voice membership. The combined reconciler also retains any required presence contribution and ignores stale event channels, so duplicate/out-of-order notifications do not reverse a newer membership state. Disabled/removed rules stop granting roles and retain durable ownership records until cleanup succeeds. Shared activity roles across source channels remain assigned when switching.

### Permissions and role ownership

The bot needs View Channel on sources and View Channel, Send Messages, Embed Links and Read Message History on destinations. Commands validate channel types, guild ownership and visible/loggable destinations. Existing Guild Voice States and Server Members intents are reused.

Activity roles additionally require **Manage Roles**, a bot role above the configured roles, and a manageable member hierarchy. The implementation conservatively skips the server owner and members at/above the bot. Managed/integration roles, @everyone, Connected, and roles granting Administrator, Manage Roles or Manage Server are rejected. Deleted/unmanageable roles and failed API calls are retried without crashing the bot. Do not change permissions automatically to resolve failures.

Before granting a missing role, Mr-Clean saves an acquisition intent in `VoiceRoleOwnerships`; after a successful API call, it confirms ownership. A pre-existing role is **never claimed** and is never removed on departure. Only a confirmed Mr-Clean grant can be removed. If an unconfirmed intent is found with a role already present after an uncertain operation/restart, ownership is relinquished and the role is left intact. This favors an occasional retained role over stripping an unrelated assignment. Cleanup records survive rule deletion, permission failures and reconnects. Missing/uncached members are not assumed disconnected; genuinely departed members are confirmed via REST before claims are discarded.

Use dedicated activity roles. Discord does not expose multiple ownership claims for a role: if a human or another bot independently re-grants an already Mr-Clean-owned role, that additional intent cannot be distinguished. A later leave can still remove the owned role. Review this limitation before configuring roles shared with other automations. The Connected role is always excluded, including from malformed persisted rules. VC Roles assignments and permissions are unchanged.

### Logging, timing and recovery

Joins and reconnects remain one plain-text line with real mentions: `🔊 <@user-id> joined <#channel-id>`. Departures remain embed-only: user/channel mention plus recorded activity label, actual connected duration and interruption count, including zero. There are no fields, titles, footers, visible log IDs or timestamps. Recovery adds a brief observation-gap note. Disabling a log removes only that message type. No voice message announces Connected-role assignments.

Duration tracking and interruption tracking are independent. Disabling duration omits the duration line and does not accumulate seconds. Disabling interruptions omits their count and starts a new session on each reconnect. Otherwise returns to the same channel within 120 seconds inclusive continue the session and exclude disconnected time. Activity name/wording, timing policy and destination are captured for a new session to avoid rewriting accounting mid-session; dynamic departure names are resolved from the rule at observation time. Logging switches take effect on subsequent events; role changes use current rules immediately. Disabling/removing a rule stops tracking an occupant on the next cache reconciliation without creating a misleading physical-departure log. Re-enabling starts/resumes tracking at observation time.

Gateway handlers copy observations/timestamps immediately and perform no database/REST work. A single processor keeps member events ordered; switches generate departure before arrival. Rules refresh immediately after successful commands and poll the database every ten seconds. Cache reconciliation detects missed departures/newly tracked occupants; uncertain departures stop at the last confirmed checkpoint. Active legacy sessions, outbox IDs and delivery destinations are retained by migration; they are not rewritten or reset. Persisted state remains scoped to this bot instance's configured guild; use a separate database for another bot instance, as before.

State and new outbox rows commit atomically. Delivery runs separately, ordered within each destination; cross-destination and role logs have no combined delivery ordering. Retry metadata stays in the database. New payloads preserve the minimal format and use a persisted history baseline to suppress duplicate retries within the latest 100 messages. Delivery is at least once; bounded history can miss a previously sent message. Older queued text is delivered verbatim. No format migration is required for existing session/outbox rows.

Run one bot instance per database/guild. A crash can lose the in-memory tail awaiting persistence; a prolonged database outage can grow that queue. Normal shutdown drains it within the host timeout. Unobserved downtime is excluded rather than invented.

### Connected role logging

Connected assignment **and** removal events now use ordinary role logging with the configured route, regardless of voice logging enablement. A read-only preflight found Listening, Watching and Other Games all routed to `#annoyinglog` (`1556935145418919937`), while Connected was routed to Music log. The migration derives the target from those existing noisy-role routes (not a hardcoded destination), validates that they agree, and updates/adds only Connected's route. Other role routes stay unchanged. If existing noisy-role routes disagree or cannot be determined, migration fails atomically and requires administrator review; it does not guess.

See [deployment and rollback instructions](docs/voice-logging-deployment.md). No deployment or live configuration changes are part of development.

## Rich Presence activity roles

Mr-Clean can centrally reconcile voice and Rich Presence triggers for the same role. It evaluates every reported Playing, Listening, Watching and Streaming activity, uses exact administrator-configured names (optionally restricted by application ID), and supports generic fallbacks. Shared roles remain until no active trigger requires them. PreMiD continues publishing to Discord; there is no direct browser-extension integration.

Presence capture and per-guild role management are **disabled by default**. Four verified existing generic role IDs are seeded disabled; no dedicated game roles or named mappings are guessed. Manage Server/Administrator commands under `/activity` provide `track`, `edit`, `untrack`, `list`, `fallback`, `enable`, `inspect`, `adopt` and `confirm-empty`. Replies and diagnostics are ephemeral. Existing role-change logging handles role messages; there is no separate activity log.

See [activity setup, API limitations and TuxedoMan cutover](docs/activity-roles.md) for exact commands, privileged intents, existing role IDs, ownership/adoption, conservative missing-presence handling and rollback. TuxedoMan and VC Roles are not changed during development. Fresh gateway activity endings (including empty/offline updates) release the presence contribution promptly; startup/disconnected cache state remains unknown. See the [presence reliability review](docs/presence-handling-review.md).

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
- per-guild voice channel rules and shared activity role ownership
- named presence rules, generic fallback settings and last required presence role IDs
- persisted voice sessions and ordered delivery queue

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
