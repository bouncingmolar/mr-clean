# Activity role hierarchy correction and enabled-state deployment

This correction is **not deployed**. The live image remains `mr-clean:admin-visibility-739e04b`. No live roles, permissions, configuration, database records, containers or gateways were changed during this work. No existing grants were adopted.

## API and Discord.Net findings

[Discord's Permission Hierarchy](https://docs.discord.com/developers/topics/permissions#permission-hierarchy) limits role grants to roles below the bot's own highest role. It separately limits kick/ban/nickname actions by the **recipient's** highest role. Those are different rules. The [Add Guild Member Role](https://docs.discord.com/developers/resources/guild#add-guild-member-role) and [Remove Guild Member Role](https://docs.discord.com/developers/resources/guild#remove-guild-member-role) endpoints require Manage Roles. Administrator does not let a bot assign a role at/above its own highest role.

The installed Discord.Net **3.18.0** was reviewed:

- [SocketGuildUser](https://github.com/discord-net/Discord.Net/blob/3.18.0/src/Discord.Net.WebSocket/Entities/Users/SocketGuildUser.cs): AddRoleAsync/RemoveRoleAsync delegate through their single-element plural operations.
- [UserHelper](https://github.com/discord-net/Discord.Net/blob/3.18.0/src/Discord.Net.Rest/Entities/Users/UserHelper.cs): those operations call the API once per role without checking the recipient's highest role/Administrator permissions.
- [DiscordRestApiClient](https://github.com/discord-net/Discord.Net/blob/3.18.0/src/Discord.Net.Rest/DiscordRestApiClient.cs): narrow PUT/DELETE `guilds/{guildId}/members/{userId}/roles/{roleId}` calls validate IDs/@everyone and leave API authorization to Discord. They do not replace a member's complete role collection.

Mr-Clean incorrectly applied an additional `member.Hierarchy < bot.Hierarchy` restriction. The shared `DiscordVoiceRoleGateway` now checks role manageability without that comparison. Assignments, removals, adoption and owned cleanup all use that same gateway. No command-specific bypass or second reconciler was added; no dependency, role-operation API or migration changed.

The existing **guild-owner exclusion remains deliberately unchanged**, as application policy rather than a claimed Discord role-operation requirement. The reported blocked user is not the owner. All other safeguards remain: connected/cached guild/member, bot Manage Roles, activity role strictly below the bot, no @everyone/managed/integration/privileged roles, protected Connected ID, explicit configuration for new grants/adoption, confirmed ownership before removal, member-specific reviewed adoption, per-member serialization, retries and voice/presence union semantics. A manually assigned existing role remains unowned until explicitly reviewed/adopted.

## Verified current live state (read-only)

On 18 October 2026 (Brisbane), the bot was running at `739e04b` with zero restarts. **Global presence-role management is true**, not disabled. All eight Playing rules and four generic fallbacks are enabled:

| Activity | Role ID | Application restriction |
| --- | --- | --- |
| Codenames | `1561090465716240448` | `817859401477259315` |
| TETR.IO | `1092394298139480146` | any |
| Sketchful.io | `1422351434501984268` | any |
| Minecraft | `1557000596647575582` | any |
| VALORANT | `1069147944227442708` | any |
| Gartic.io | `1069202917283868742` | any |
| Gartic | `1069579310022807672` | any |
| Among Us | `1073952995357753354` | `477175586805252107` |

The generic roles remain Other Games `1069145493852131329`, Listening `1069145494674214973`, Watching `1069145496054153236`, Streaming `1069145496985284648`. Codenames voice channel `1126902663607484476` shares role `1561090465716240448`; the other three voice activity-role lists are empty. All four voice log destinations are preserved. At inspection, one confirmed Minecraft owned grant existed. Ownership/contribution counts are operational state and can legitimately evolve while management is enabled; preserve the actual records, do not recreate them from this count.

Both prior rollback containers are stopped with restart disabled:

- `mr-clean-rollback-dacd084-admin-visibility`
- `mr-clean-rollback-bee7e73-presence-dacd084`

Private backup checksums in `/home/tim/.local/share/mr-clean/deployments/admin-visibility-739e04b` and `presence-dacd084` pass. These predate the latest complete staged configuration/enablement; take a new backup at any approved deployment. Existing backups and containers must remain untouched.

## Minimal future deployment — requires separate approval

1. Confirm the approved fix commit, clean tree, 173 passing tests and successful Release build. Freeze administrator edits briefly for a consistent before/after comparison. Refresh the live snapshot; do **not** assume the earlier disabled-state deployment steps apply. Do not toggle management, replay track/fallback/voice edits or modify role hierarchy/permissions.
2. Create a new 0700 private directory outside Git/build context, e.g. `/home/tim/.local/share/mr-clean/deployments/role-hierarchy-<approved-short-commit>`. Save the current container inspection/environment and image digest, logs and read-only configuration snapshot. Use owner-only 0600 files. Reuse the proven backup/build approach in [the visibility deployment guide](admin-command-visibility.md), with a new directory/name and the current image digest `sha256:972614bfc5798c9c21d721eee527aaa3ddb3957c2ff0fafd0ee012b77fc2d86f`; **do not use its old dacd084 digest assertion or assume management false**.
3. Take a fresh PostgreSQL custom-format dump with the existing database container, verify `pg_restore --list`, full decode to `/dev/null`, and checksums. Preserve the working image archive. Snapshot all eight ActivityRules, four ActivityFallbacks, ActivityGuildSettings, four VoiceChannelRules, all VoiceRoleOwnerships/ActivityMemberStates, migration history and role-log/moderation settings. Capture the enabled flag as **true** and preserve it unchanged. Preserve runtime environment exactly, including ObservePresences=true, guild, token, connection string, voice settings and logging.
4. Build from `git archive <approved-fix-commit>` into the new private `source` subdirectory; label/tag the candidate with that exact commit. Secrets/backups stay outside the source build context. No schema/model/migration changes exist. Do not run a separate migration container or manual database update.
5. Gracefully stop only `mr-clean-dev`, disable its restart policy and preserve it as **`mr-clean-rollback-739e04b-member-hierarchy`** (first verify that name is unused). Leave both older rollback containers untouched. Start one new `mr-clean-dev` with the saved unchanged environment, network `mr-clean-net`, restart policy `unless-stopped`, stop timeout 60 seconds, and approved candidate image. Do not restart PostgreSQL, VC Roles, dr-time or GGaudio. Keep TuxedoMan unable to manage roles.
6. Verify Connected/Ready, avatar/database initialization, no errors/restarts and no new migration history entries. Compare the full staged settings and permission snapshots, including the **true** management flag, all role IDs/application restrictions and Codenames shared binding. Verify voice persistence/outbox and existing moderation/role-log routes. Ordinary owned-role/contribution changes after startup can be expected; existing ownership must not be dropped or reconstructed.
7. **Expected effect of preserving enabled management:** newly eligible higher-ranked members may receive missing configured activity roles as normal reconciliation resumes. That is acquisition of missing grants, not adoption of existing grants. Existing unowned stale roles remain until the single reviewed adoption below. Owned roles may now be cleaned up where the old gateway incorrectly refused. Capture before/after ownership and actual role changes rather than demanding that operational counts stay frozen. No automatic bulk/member adoption is part of startup or deployment.
8. Stop at verification and the approved manual test. Do not retire TuxedoMan, alter its permissions, adopt other members or delete roles as a side effect of this correction. A later full adoption/cutover remains separately reviewed.

The usual application-only replacement command shape, **not executed during development**, is:

```text
docker update --restart=no mr-clean-dev
docker stop --time 60 mr-clean-dev
docker rename mr-clean-dev mr-clean-rollback-739e04b-member-hierarchy
docker run -d --name mr-clean-dev --network mr-clean-net --restart unless-stopped --stop-timeout 60 --env-file <new-private-directory>/runtime.env <approved-image-digest>
```

## One reviewed stale-grant test after deployment approval

Run commands manually in Discord; Codex text does not execute slash commands. Roles/members must be native selections. No auto-adoption or member-list-wide adoption script is authorized.

1. `/activity list` and `/voice list`: verify management enabled, Codenames's strict application match and shared voice role. The current deployment is already enabled; do not repeat the enable command as a test.
2. `/activity inspect member:@you`: require a fresh observed Playing Codenames activity with application `817859401477259315` and no Gartic.io activity (unless intentionally still active). An unknown/incomplete observation is not enough evidence to remove the old role. Verify that your sole stale Playing Gartic.io grant is the intended reviewed grant.
3. Run **once**:

```text
/activity adopt role:<@&1069202917283868742> member:@you confirm:true
```

4. Expect **one eligible grant adopted**. Zero is a stop-and-investigate result, not grounds for bulk adoption or repeated retries. Adoption records ownership; it does not directly remove the role. Allow the next fresh event or configured reconciliation sweep (normally up to 30 seconds plus database/API latency). Playing Gartic.io should disappear; Playing Codenames should be assigned/retained while either the matching presence or Codenames voice trigger remains. Preserve unrelated roles, the community codenames role `963037960230551582`, Connected and VC Roles.
5. Check existing role-change logs and no repeated/flapping API calls. End presence first while voice remains, then leave voice; repeat with voice ending first. The shared role remains until neither trigger requires it. Do not use confirm-empty while a supported activity is actually reported. There is no arbitrary expiry timer in this test.

## Immediate rollback

On startup/check failure, stop/disable restart on the new candidate before starting preserved `mr-clean-rollback-739e04b-member-hierarchy` with its original image/environment/restart policy. Retain the failed candidate/logs and every earlier rollback artifact. Keep all configuration, ownership/contributions, role objects and migration history; do not restore the whole database, run Down migrations or mass-remove member roles. Leave management **true** unless the operator separately approves a pause.

The old 739e04b reintroduces the overly restrictive member-rank check. Consequently a newly acquired/adopted higher-member grant may remain stale while rolled back; its ownership record must survive for a later corrected reconciliation. Do not claim rollback automatically cleans those grants or removes independent manual intent. Review any manual cleanup separately. Keep TuxedoMan's permissions revoked; no automatic handback to that bot is part of this patch rollback.

## Validation

173 tests pass (159 previous + 14 new hierarchy/safety cases), none failed/skipped. New tests use the production role-permission gate through built-in .NET interface proxies and the existing EF in-memory reconciler: higher-ranked Administrator voice/presence acquisition/removal, a single reviewed stale-game adoption while retaining the current game, shared-trigger ending orders, strict equal/above role rejection, protected roles, missing permissions, owner policy, retained cleanup intent and unknown-presence protection. Existing ownership/retry/concurrency, role routing, public/private administration, voice output/accounting, moderation/avatar/retention and migration/model tests remain green. Release build succeeds with **0 warnings and 0 errors**. Local documentation links and whitespace checks pass. Discord API acceptance is not claimed as live-tested; the approved real member-specific test remains necessary.
