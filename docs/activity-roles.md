# Activity roles and TuxedoMan cutover

This release extends the existing voice rules and ownership reconciler. It does not introduce a second bot framework, a PreMiD integration, or separate activity-message logs. Presence capture and guild presence-role management are disabled by default. Nothing in development enables/disables TuxedoMan or VC Roles.

## Reference review and API findings

The requested TuxedoMan files were reviewed before implementation:

- [presenceUpdate.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/events/presenceUpdate.ts)
- [game-manager.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/modules/game-manager/game-manager.ts)
- [roles.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/modules/discord/roles.ts)
- [activity.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/modules/util/activity.ts)
- [job-queue.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/modules/util/job-queue.ts)
- [ready.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/events/ready.ts)
- [track.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/commands/track.ts)
- [index.ts](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/index.ts)

Useful ideas retained are explicit name registration, aliases sharing a role, generic categories, serialization and startup reconciliation. Mr-Clean improves these by evaluating every supported activity, combining independent triggers and issuing narrow role add/remove calls. Untracking never deletes a Discord role. No source files were copied into this repository.

Discord.Net 3.18.0 provides `PresenceUpdated`, `SocketPresence.Activities` and `RichGame.ApplicationId`. An application ID is optional; not every activity supplies one. REST member endpoints cannot reconstruct presence or activity history. PreMiD publishes through Discord; Mr-Clean reads the reported activity name/type, not the browser tab title or website URL.

[Discord gateway documentation](https://docs.discord.com/developers/events/gateway#privileged-intents) requires privileged intents to be enabled in the Developer Portal before requesting them. [Presence event documentation](https://docs.discord.com/developers/events/gateway-events#presence-update) describes current activities and optional application IDs. [Discord.Net 3.18 SocketPresence source](https://github.com/discord-net/Discord.Net/blob/3.18.0/src/Discord.Net.WebSocket/Entities/Users/SocketPresence.cs) shows that absent and empty activity arrays are both exposed as empty collections. The public collection cannot reveal whether a malformed raw payload omitted that field. However, Discord documents `activities` as a required field of a fresh Presence Update, containing current activities. The warning about optional fields concerns the nested **user object**, not the activity list. Fresh gateway events therefore establish activity state, including an empty list; startup/reconnect cache reads do not. See [the termination review](presence-handling-review.md) for the exact library paths and limitations.

## Two enablement gates

Runtime configuration:

```json
"ActivityRoles": {
  "ObservePresences": false,
  "ReconcileSeconds": 30
}
```

`ActivityRoles__ObservePresences=true` requests `GatewayIntents.GuildPresences` and enables private observation/diagnostics during an **approved future deployment**. It does not itself enable presence role assignments. The second gate is a per-guild database setting, changed with `/activity enable`. The migration seeds it as false. This permits a short, assignment-disabled verification phase within the coordinated cutover window while TuxedoMan remains responsible for roles; it is not a separate long-term coexistence deployment.

In the Discord Developer Portal, select the Mr-Clean application → **Bot → Privileged Gateway Intents**:

1. Enable **Presence Intent** before deploying with ObservePresences=true.
2. Keep **Server Members Intent** enabled: membership/role reconciliation and member downloads require it.
3. Keep the existing **Message Content Intent** setting for moderation; this change does not remove that dependency.

Verified applications may need Discord approval for privileged intents. A requested but unauthorized intent can prevent gateway login; follow the deployment rollback gate. Portal toggles were not changed or independently verified by development. With ObservePresences=false, the existing gateway intent selection is preserved.

Required server permissions remain View Channel on sources and View Channel/Send Messages/Embed Links/Read Message History on voice log destinations. Role management/creation requires **Manage Roles** and a bot role above every managed role. @everyone, integration-managed roles, Connected and roles granting Administrator, Manage Server or Manage Roles are rejected. Members at/above the bot and the server owner are conservatively skipped. The existing bot's Manage Roles permission was verified previously; final role positions must be checked before configuring individual roles.

## Administrator commands

All `/voice` and `/activity` commands require Manage Server or Administrator, verified server-side by both dispatcher and handler. They are guild-only, reject other guilds and reply ephemerally. Mr-Clean retains its existing single-configured-guild runtime model; database settings and ownership are guild-scoped.

```text
/activity inspect member:@dr.tim
/activity list
/activity track name:Codenames type:Playing role:@Playing-Codenames
/activity track name:"Codenames Online" type:Playing role:@Playing-Codenames
/activity track name:Codenames type:Playing create-role:"Playing Codenames"
/activity edit id:1 application-id:123456789012345678
/activity edit id:1 clear-application:true role:@Playing-Codenames
/activity edit id:1 enabled:false
/activity untrack id:1
/activity fallback type:Playing enabled:true role:@Other-Games
/activity fallback type:Streaming enabled:true create-default:true
/activity enable enabled:true confirm-cutover:true
/activity enable enabled:false
/activity adopt role:@Other-Games member:@dr.tim confirm:true
/activity adopt role:@Other-Games confirm:true
/activity confirm-empty member:@dr.tim confirm:true
```

These are examples, not commands executed during development. The Codenames name and application ID must be measured with `/activity inspect`; no named activity is seeded. A track requires either an existing role selection or an explicit create-role request. Multiple exact names can share a role through separate rules. Name comparison is case-sensitive and ordinal. An optional application ID is an additional restriction: name, type **and** application ID must match. Rule IDs come from `/activity list`. Edit/untrack IDs are scoped to the current guild.

Supported categories are Playing, Listening, Watching and Streaming. Rich Presence custom websites normally report one of these categories. Discord Custom Status (type 4), Competing, browser titles and activity details are not alternate implicit matches; inspect first. Custom Status itself is not mapped by this release. Exact names should be copied from the diagnostic JSON string (including capitalization); do not substitute a website title.

`/activity fallback` configures the role for unmatched activities of that type. Explicit rules have priority **per activity**, not per member. Playing Codenames plus an unrelated untracked game can require both Playing Codenames and Other Games. Multiple unmatched games do not cause a generic role to flap when only one stops. Disabled rules do not count as explicit matches; an enabled fallback can then apply. Disabled fallback categories contribute nothing.

`/activity list` reports capture state, guild management state, all explicit rules and fallback settings. `/activity inspect` reports only the selected member's latest activity names, types and available application IDs, with an observed/unknown label. It emits no public messages and stores no presence history. Diagnostic activity names are not written to application logs.

### Automatic creation/reuse

Automatic creation happens **only** after create-role or create-default is explicitly selected. A suitable role with exactly the requested name is reused. Multiple matches or an unsafe match require selecting the exact existing role; Mr-Clean will not create another role to bypass ambiguity. Creation requests are serialized per guild, and newly created roles are accounted for before their gateway cache event arrives. Created roles have zero permissions, are not mentionable and are not hoisted automatically. Mr-Clean never reorders or deletes them.

If creation has an uncertain network outcome, subsequent automatic creation is blocked for that name in this process until an existing match can be found/selected. Definite validation or permission rejection is retryable after correction. A role can remain if creation succeeds but configuration persistence fails; inspect and reuse it, do not repeatedly create. Discord has no atomic unique-role-name API: simultaneous creation by another bot/administrator remains outside Mr-Clean's local lock. Configuration removal never deletes roles.

## Combined role ownership

The single reconciler computes:

```text
required roles = enabled voice-rule roles UNION active presence-rule/fallback roles
```

The same ownership ledger (`VoiceRoleOwnerships`, retained for backwards compatibility) covers both sources. Per-guild/per-member locks serialize reads and mutations across gateway events, commands, retries and adoption. Different members do not share that mutation lock. Member voice/roles and the latest presence snapshot are read inside the lock, rather than trusting an old event's channel or activity. A three-second in-memory acknowledgement overlay prevents immediate duplicate calls while a successful REST mutation awaits its member-update gateway echo; later reconciliation rechecks actual roles. Individual Discord AddRole/RemoveRole operations preserve unrelated roles; the entire role collection is never replaced.

Pre-existing roles remain unowned. A missing role gets a durable acquisition intent before Discord is called, then confirmed ownership after success. Only confirmed ownership allows removal. An ambiguous pending acquisition with a role already present is relinquished rather than stripped. Shared voice/presence roles remain while either trigger requires them.

Presence contributions persist only the last required **role IDs**, never activity names, websites, details or a timeline (`ActivityMemberStates`). They survive restart so a missing cache cannot make the voice path remove a presence-required role. Unknown snapshots conservatively preserve possible owned contributions; a partial positive startup cache may add evidence but cannot erase other contributions. Disabling/removing a configured category/rule can release its contribution, provided another trigger does not require the same role. New rules and role edits re-evaluate the latest known observation.

Pausing `/activity enable enabled:false` freezes existing presence contributions: it neither strips present presence-only grants nor re-adds absent grants. It also freezes mutations for roles registered in enabled presence rules/fallbacks, even when those roles have voice bindings. This prevents an overlapping voice binding from competing with TuxedoMan during observation/pause. Voice-only roles remain independent. Disabling presence capture has the same protection. Ownership is retained for approved resumption; pausing does not transfer ownership. Keep shared voice bindings empty until cutover, and do not untrack/disable overlapping rules during coexistence: removing that registration also removes its pause protection.

Use dedicated automation roles. Discord cannot represent independent ownership of the same member-role grant. If another actor manually re-grants a role already owned by Mr-Clean, that additional intent is indistinguishable; later removal can still occur. Bulk adoption explicitly opts into ownership of existing grants and therefore needs a reviewed holder list. Prefer member-specific adoption for roles that may also have manual grants. Connected (`894787914599370803`) is always protected and cannot be configured/adopted as an activity role.

## Missing presence, reconciliation and privacy

Gateway notifications only copy observations and wake a coalesced member queue; they do not await database writes or Discord APIs. A fallback reconciliation runs every 30 seconds by default (10–300 configurable) to retry permission/rate-limit failures and discover missed voice/role events. Events normally process only dirty members; startup/configuration changes/retry passes also include owned members and saved contributions. There is no REST polling for presence, which REST cannot provide. Existing voice logging keeps its own 10-second checkpoints and 120-second reconnect window unchanged.

Each fresh `PresenceUpdated` event copies the complete current activity list immediately and wakes reconciliation. An empty list clears presence contributions on the next event-driven pass, without a second frame or expiry timer. This also applies to fresh offline updates and empty `client_status`; these are member states, not evidence that the bot's gateway is unavailable. Remaining voice triggers or other simultaneous activities still retain the corresponding role.

On bot gateway disconnect/guild unavailability, observations become unknown. Startup cached presence (including an empty offline cache entry) cannot prove inactivity. Positive cached activities may seed incomplete evidence. Persisted contributions survive unknown state until a fresh event, a relevant configuration change, or an explicit administrator decision. Discord REST cannot recover activities that ended during downtime and never produce another fresh presence event.

`/activity confirm-empty member:... confirm:true` remains a recovery tool after independently checking inactivity, not the normal termination path. It refuses while a supported activity is still cached, cannot override an active voice trigger, and is superseded by a later presence event. Confirmed guild departures clear contribution/ownership state without role mutations on departed members.

No raw activity history is persisted. Memory holds only the latest minimal snapshot; it is invalidated on disconnect and removed on member departure. Unknown state can delay removals indefinitely rather than risk stripping a still-required shared role. API permission failures are retried with retained ownership; role hierarchy failures never cause permission changes. No historical presence or downtime activity can be reconstructed reliably.

## Verified current role IDs and logging

A read-only Discord role inventory found:

| Category | Existing role ID | Migration state |
| --- | --- | --- |
| Playing / Other Games | `1069145493852131329` | disabled |
| Listening | `1069145494674214973` | disabled |
| Watching | `1069145496054153236` | disabled |
| Streaming | `1069145496985284648` | disabled |

These are configuration seeds, not runtime name-based selection. No duplicate generic roles are created. The deployment decision is to create a dedicated Playing Codenames role after measuring the exact PreMiD activity name/type, and bind its new ID to both presence and Codenames voice. The existing community codenames role `963037960230551582` is excluded. No creation has been executed. The [environment run sheet](eggdraw-pi-cutover-checklist.md#block-e--stage-reviewed-rules-then-transfer-authority-in-discord) contains the seven actual `tx roles` mappings with Discord-verified existing role IDs and exact staging commands.

Existing role-change logging remains the only source of activity-role addition/removal messages. Existing Other Games/Listening/Watching routes point to `#annoyinglog` (`1556935145418919937`) and remain unchanged; Streaming keeps its current route/default unless an administrator changes it. The pending voice-rules migration restores Connected logs by deriving that destination from the existing noisy-role routing. Connected was still suppressed and routed to Music in the live `bee7e73` configuration during development; no live routing was changed. VC Roles remains its assignment owner.

## Database migrations and deployment gates

From deployed `bee7e73`, apply these only in an approved deployment:

1. `20261017130932_VoiceChannelRules` (previous feature commit): seeds the four existing voice rules without role assignments and restores Connected routing as documented in the voice deployment guide.
2. `20261017134212_PresenceActivityRules`: adds `ActivityRules`, `ActivityFallbacks`, `ActivityGuildSettings` and `ActivityMemberStates`, with the four verified generic roles seeded disabled and guild management disabled. It does not rewrite voice sessions/outbox rows or other role routes. No named activity is guessed or seeded. The seven administrator-exported named-game mappings are configured through `/activity track` during the gated deployment, using existing IDs in the environment run sheet; they are not new hardcoded migration/runtime defaults. Codenames is created/configured explicitly only after discovery. This documentation update adds no migration and does not change existing migrations.

All role ownership and current voice session data remain in the existing database. No live migrations or data changes are part of development. Preserve a private predeployment backup and both the current image and runtime environment. Do not run schema Down migrations as application rollback; they discard configuration/ownership.

## Single coordinated TuxedoMan transition (requires explicit operator approval)

The first approved presence-management deployment will transfer role management from TuxedoMan to Mr-Clean in one maintenance window. There is no planned long-term coexistence or separate observation-only release. A brief preparation/diagnostic phase in that same window keeps Mr-Clean's guild management disabled while TuxedoMan remains responsible for roles. Stop TuxedoMan's management and verify it cannot resume before enabling Mr-Clean and adopting existing grants.

Follow the authoritative [coordinated cutover checklist](tuxedoman-cutover.md), including its inventory, enable/adopt commands, manual acceptance matrix and phase-specific rollback. The simple pause safeguard remains a deployment interlock; it is not a dual-manager protocol. The [eggdraw-pi run sheet](eggdraw-pi-cutover-checklist.md) specifies reversible member-only permission revocation for the public bot, then final guild removal only after acceptance and a verified export/restore plan. Never delete its existing activity roles. VC Roles remains responsible for Connected.

The lifecycle investigation and fixes are complete in `75ede5e`: 143 tests pass and the Release build has zero warnings/errors. No deployment or live changes were performed. The seven TuxedoMan named-game mappings are now supplied and their role IDs verified. Remaining checks include the Codenames PreMiD name/type, generic category enable flags, export completeness/event-role scope, role ownership review and final Portal settings.
