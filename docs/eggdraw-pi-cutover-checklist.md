# eggdraw-pi coordinated cutover run sheet

Prepared 18 October 2026 (Australia/Brisbane) for candidate **`dacd084`**, using read-only Docker, PostgreSQL and Discord GET checks. **No mutating preparation/cutover/build/rollback block below was executed; only read-only audit checks were performed.** Run them only after explicit approval of the maintenance window. This environment-specific checklist supplements [the coordinated guide](tuxedoman-cutover.md); the TuxedoMan retirement here uses Discord actions, not a presumed local service.

## Verified deployment facts

- Host `eggdraw-pi`, ARM64; repository `/home/tim/mr-clean`, branch `feature/voice-activity-logging`, clean at `dacd084` when inspected. Existing implementation validation: 143 tests, Release build zero warnings/errors.
- Running bot `mr-clean-dev`: image `sha256:21ce4a74567547d0662ac70482609475669312978afe89ba6e88dad13d2da2a7`, tagged `mr-clean:voice-bee7e73`. Preserved deployment result/image metadata independently associate it with `bee7e73`; image has no original commit label. Running since 17 October 2026 22:41 Brisbane, zero restarts/OOMs, startup gateway Connected/Ready and avatar initialization for 249 members. No fail/error/unhandled-exception markers in current container logs. No Docker HEALTHCHECK exists, so these are operational checks, not a Docker health status.
- Restart policy `unless-stopped`; stop timeout 60 seconds; network `mr-clean-net`; no bind mounts, volumes, published ports, resource limits or special security overrides on the bot. Default `json-file` logging. `DOTNET_ENVIRONMENT=Development` and explicit guild override `683633975838769192` must be preserved. Voice logging is enabled by environment despite image JSON default false; Connected protection is set to `894787914599370803`.
- PostgreSQL container `mr-clean-db`, `postgres:16`, network `mr-clean-net`, persistent volume `mr-clean-postgres-data`. Database/user `mrclean`; read-only queries succeed, bot has an idle connection, schema USAGE/CREATE checks pass and all eight public tables are owned by `mrclean`. Migration history ends at `20261017120000_AddVoiceActivityState`. One guild voice-state row and zero queued voice-log messages were present. Two new migrations are pending; none was applied.
- Only `mr-clean-dev` among running Docker containers uses this bot token. Host-wide/remote absence cannot be proven solely by Docker; do not launch another instance elsewhere.
- Durable artifacts exist under `/home/tim/.local/share/mr-clean/deployments/voice-c187b2c` and `voice-bee7e73`: directories 0700, files 0600, stored checksums match and all three PostgreSQL archives pass `pg_restore --list`. These are historical backups, not a substitute for a fresh cutover backup. No isolated restore rehearsal was performed. The saved `voice-bee7e73/candidate-inspect.json` image/environment match the current running bot. **`mr-clean-prev-voice-bee7e73` is the older pre-bee container**, not the current bee7e73 rollback target. Preserve the currently running container at cutover as shown below. Approximately 99 GiB disk space was available.
- All four source voice channels and three destination text channels exist. Bot effective permissions allow source View Channel and destination View Channel/Send Messages/Embed Links/Read Message History. Mr-Clean currently has Administrator/Manage Roles and hierarchy position 245; this plan adds no permissions. Existing voice mappings remain General dynamic → `806802061693550652`, Music fixed → `943558876706533477`, Gartic Phone/Codenames fixed → `1556935771049689119`.
- Mr-Clean application flags `2662400` include Presence Limited, Guild Members Limited and Message Content Limited. This is positive API evidence that the privileged intent settings are enabled for the application, per [Discord's flags](https://docs.discord.com/developers/resources/application#application-object-application-flags). Recheck Bot settings manually; the live old binary does not request the new presence intent, so actual reception still needs the candidate diagnostic.

## TuxedoMan and role inventory: verified versus unknown

TuxedoMan member/application **`441366628836442122`** matches the [public bot invitation](https://github.com/alex-taxiera/TuxedoMan/blob/master/readme.md); its public application reports `bot_public=true`. No TuxedoMan container, installed systemd unit, matching local runtime directory or deployment configuration was found in the inspected locations. The local Node bots identified are `/home/tim/dr-time` and `/opt/GGaudio`; do not stop them. This strongly supports treating TuxedoMan as an externally hosted installation, but its physical hosting/owner cannot be proved from guild REST. **There is no verified local TuxedoMan stop command.**

Its current member roles are:

| ID | Name | Relevant permission |
| --- | --- | --- |
| `767344292284334090` | RoleBot | Manage Roles |
| `1324170106099597372` | tuxbot | Manage Roles |
| `694926016585400400` | Вot (Cyrillic first letter) | Neither Manage Roles nor Administrator |
| `733578006127312957` | Bots | Neither |
| `1069145496054153236` | Watching | Neither |

@everyone also lacks Manage Roles/Administrator. Therefore removing **both RoleBot and tuxbot from the TuxedoMan member only** prevents its role mutations while retaining its membership/configuration for a reversible test window. Never edit/delete shared role permissions or change VC Roles' assignments. Recheck effective permissions at transfer time; if another granting role has appeared, stop and review. Discord administrator permission bypasses channel denies, so channel overrides alone are not a stop mechanism.

| Existing automation role | Verified ID | State/configuration still needed |
| --- | --- | --- |
| Other Games | `1069145493852131329` | 15 current holders including any bots; enable Playing fallback as intended, review human adoption scope. |
| Listening | `1069145494674214973` | 2 holders; recover TuxedoMan enable flag. |
| Watching | `1069145496054153236` | 7 holders including bots; recover enable flag; bulk adoption excludes bots. |
| Streaming | `1069145496985284648` | 4 holders; recover enable flag and existing/default route. |

The administrator supplied the following **actual `tx roles` export**. All seven role IDs were independently verified with a Discord guild-roles GET on 18 October 2026; each name has exactly one match, each role is non-managed with zero permissions. These tracked-game mappings use Playing, matching TuxedoMan's named-game handling. Activity names come from the export, not from role-name inference. Reuse these IDs without creating, renaming or replacing roles.

| Existing role name | Verified role ID | Exact activity name | Type |
| --- | --- | --- | --- |
| Playing TETR.IO | `1092394298139480146` | `TETR.IO` | Playing |
| Playing Sketchful.io | `1422351434501984268` | `Sketchful.io` | Playing |
| Playing Minecraft | `1557000596647575582` | `Minecraft` | Playing |
| VALORANT | `1069147944227442708` | `VALORANT` | Playing |
| Playing Gartic.io | `1069202917283868742` | `Gartic.io` | Playing |
| Playing Gartic Phone | `1069579310022807672` | `Gartic` | Playing |
| A Mong Goose | `1073952995357753354` | `Among Us` | Playing |

**Codenames decision is settled:** create a dedicated `Playing Codenames` role during the approved window, once `/activity inspect` confirms the exact PreMiD name/type. No such role currently exists; its ID is not available until creation. Bind that same new ID to presence and Codenames voice source `1126902663607484476`. Never reuse/adopt the 37-holder community `codenames` role `963037960230551582`. Creation is planned, not performed. If a suitable dedicated role has appeared before deployment, inspect and safely reuse it rather than create a duplicate.

Existing Other Games/Listening/Watching role routes are Annoyinglog **`1556935145418919937`**. Connected currently routes to Music and is suppressed by old bee7e73; the pending voice migration derives the common Annoyinglog route and restores its logging. Existing named-role routes retain their current configuration; do not infer a route from a similar role name. The seven named mappings are now established by the administrator's export. No private TuxedoMan database was accessed, and generic category enable flags/event-role settings still require confirmation.

### Manual discovery required before final approval

The named-game export is now supplied and recorded above; do not rediscover/guess those seven names. The seven supplied entries are the named-game transfer set; any later additions must be explicitly documented. Generic flags and event-role settings are not part of `tx roles`. Obtain those through a non-mutating settings display or the bot operator. Do not toggle `settings`, run `setup`, `track` or `untrack` just to inspect: those mutate configuration, can create roles, or reconcile members. Preserve the supplied export, category flags and reviewed human holder/adoption lists privately for rollback.

TuxedoMan also supports event roles; confirm whether that functionality is enabled/in use. Mr-Clean's current release does not replace TuxedoMan event-role automation. Existing event roles remain, but retiring the bot would stop that automation. This is a scope blocker if used.

Before final bot removal, confirm whether TuxedoMan retains guild tracking data after removal/reinvite. If unknown, obtain a complete export and a tested/documented manual restoration procedure; an invite link alone does not restore its private mappings. Its public original invite requests permissions `268443648`; record additional member assignments above and role hierarchy. Do not assume reinviting automatically restores custom roles/settings.

## Block A — future fresh backup and preserve configuration (no bot stop)

Run in bash as `tim` only when approved. The fixed new directory must not already exist; a partial run requires inspection, not blind overwrite. Secrets stay in 0700/0600 private storage outside Git and the source build context. Never enable shell tracing or paste these files into chat.

```bash
set -euo pipefail
umask 077
cd /home/tim/mr-clean
export VOICE_CUTOVER_DIR=/home/tim/.local/share/mr-clean/deployments/presence-dacd084
export VOICE_ROLLBACK=mr-clean-rollback-bee7e73-presence-dacd084
export VOICE_IMAGE=mr-clean:presence-dacd084
if [ -e "$VOICE_CUTOVER_DIR" ]; then
  echo 'Cutover directory already exists; inspect the prior run before continuing.' >&2
  exit 1
fi
mkdir -m 700 "$VOICE_CUTOVER_DIR"
if docker container inspect "$VOICE_ROLLBACK" >/dev/null 2>&1; then
  echo 'Rollback container name already exists; stop and review.' >&2
  exit 1
fi
test "$(docker inspect --format '{{.State.Running}}' mr-clean-dev)" = true
test "$(docker inspect --format '{{.Image}}' mr-clean-dev)" = sha256:21ce4a74567547d0662ac70482609475669312978afe89ba6e88dad13d2da2a7
docker inspect mr-clean-dev > "$VOICE_CUTOVER_DIR/original-container.json"
docker inspect mr-clean-db > "$VOICE_CUTOVER_DIR/database-container.json"
docker inspect --format '{{.Image}}' mr-clean-dev > "$VOICE_CUTOVER_DIR/original-image.txt"
docker logs mr-clean-dev > "$VOICE_CUTOVER_DIR/original.log" 2>&1
python3 - <<'PY'
import json, os, pathlib
p = pathlib.Path(os.environ['VOICE_CUTOVER_DIR'])
c = json.loads((p/'original-container.json').read_text())[0]
e = dict(v.split('=', 1) for v in c['Config']['Env'])
assert e['Discord__GuildId'] == '683633975838769192'
assert all('\n' not in v and '\r' not in v for v in e.values()), 'Docker env-file cannot safely preserve multiline values'
(p/'original.env').write_text(''.join(f'{k}={v}\n' for k,v in e.items()))
# Copy the existing working environment; add only approved feature settings.
e.update({'ActivityRoles__ObservePresences':'true', 'ActivityRoles__ReconcileSeconds':'30',
          'VoiceLogging__Enabled':'true', 'VoiceLogging__ConnectedRoleId':'894787914599370803',
          'VoiceLogging__ReconnectWindowSeconds':'120'})
(p/'candidate.env').write_text(''.join(f'{k}={v}\n' for k,v in e.items()))
PY
docker exec mr-clean-db sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$VOICE_CUTOVER_DIR/postgres.dump"
docker exec -i mr-clean-db pg_restore --list < "$VOICE_CUTOVER_DIR/postgres.dump" > "$VOICE_CUTOVER_DIR/archive-list.txt"
docker exec mr-clean-db sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --data-only --table=\"RoleLogRoutes\" --table=\"RoleLoggingConfigurations\"' > "$VOICE_CUTOVER_DIR/role-routing.sql"
docker image save "$(cat "$VOICE_CUTOVER_DIR/original-image.txt")" | gzip > "$VOICE_CUTOVER_DIR/original-image.tar.gz"
chmod 600 "$VOICE_CUTOVER_DIR"/*
(cd "$VOICE_CUTOVER_DIR" && sha256sum postgres.dump original.env original-container.json original-image.tar.gz > sha256sums.txt && sha256sum -c sha256sums.txt)
gzip -t "$VOICE_CUTOVER_DIR/original-image.tar.gz"
```

The route dump uses the verified live table names `RoleLogRoutes` and `RoleLoggingConfigurations`. Abort if backup/dump/checksum fails. The fresh dump is a recovery artifact; do not restore it over production for ordinary application rollback. A pre-stop dump can miss subsequent writes; take another custom-format dump after gracefully stopping the bot if the approval requires an exact stopped-bot baseline (other database writers must be accounted for).

## Block B — build exact approved source, never start it for inspection

```bash
mkdir -m 700 "$VOICE_CUTOVER_DIR/source"
git archive dacd084 | tar -x -C "$VOICE_CUTOVER_DIR/source"
docker build --label "org.opencontainers.image.revision=$(git rev-parse dacd084)" \
  -t "$VOICE_IMAGE" -f "$VOICE_CUTOVER_DIR/source/MrClean/Dockerfile" "$VOICE_CUTOVER_DIR/source"
docker image inspect --format '{{.Id}}' "$VOICE_IMAGE" > "$VOICE_CUTOVER_DIR/candidate-image.txt"
```

Source is exactly dacd084, even if a later documentation-only commit is checked out. Backup/configuration files are outside the `source` build context. Build failure leaves the working deployment untouched. Do not start the candidate before cutover: startup automatically migrates the database and connects to Discord.

## Block C — read-only baseline / repeatable status checks

```bash
docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}'
docker inspect --format 'state={{.State.Status}} restarts={{.RestartCount}} image={{.Image}} restart={{.HostConfig.RestartPolicy.Name}}' mr-clean-dev
docker exec -i mr-clean-db sh -c 'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
BEGIN READ ONLY;
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;
SELECT "RoleId", "ChannelId" FROM "RoleLogRoutes" ORDER BY "RoleId";
SELECT count(*) AS guild_state_rows FROM "VoiceActivityStates";
SELECT "Destination", count(*) FROM "VoiceLogMessages" GROUP BY "Destination";
COMMIT;
SQL
```

A voice-state row contains serialized guild sessions, so its row count is not a count of connected members. Save private holder/configuration inventories before proceeding. Recheck Portal Bot → Privileged Gateway Intents (Presence, Server Members, existing Message Content), manual adoption decisions, complete TuxedoMan export and role/event scope. No Portal change currently appears necessary from API flags, but do not rely on old-binary startup as proof of presence reception.

## Block D — approved Mr-Clean cutover, presence assignment still disabled

Stop only Mr-Clean. Do not stop PostgreSQL, dr-time, GGaudio or VC Roles. TuxedoMan still handles its roles during this short same-window preparation.

```bash
docker update --restart=no mr-clean-dev
docker stop --time 60 mr-clean-dev
docker rename mr-clean-dev "$VOICE_ROLLBACK"
# Optional exact stopped-bot baseline, retained separately from the earlier dump:
docker exec mr-clean-db sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$VOICE_CUTOVER_DIR/postgres-after-stop.dump"
docker exec -i mr-clean-db pg_restore --list < "$VOICE_CUTOVER_DIR/postgres-after-stop.dump" > "$VOICE_CUTOVER_DIR/archive-after-stop.txt"
docker run -d --name mr-clean-dev --network mr-clean-net \
  --restart unless-stopped --stop-timeout 60 \
  --env-file "$VOICE_CUTOVER_DIR/candidate.env" "$(cat "$VOICE_CUTOVER_DIR/candidate-image.txt")"
docker logs --tail 150 mr-clean-dev > "$VOICE_CUTOVER_DIR/candidate-startup.log" 2>&1
docker inspect --format 'state={{.State.Status}} restarts={{.RestartCount}} error={{.State.Error}}' mr-clean-dev
```

Allow startup to complete and inspect the private log for both migrations, Connected/Ready, intended guild/avatar initialization, database and permission errors. Re-run Block C. Migrations must now include `20261017130932_VoiceChannelRules` and `20261017134212_PresenceActivityRules`. Do not run a separate migration container or `database update` against production. Failure means immediate Block G rollback, leaving TuxedoMan's permissions unchanged.

Manual Discord actions (Manage Server/Administrator; responses are ephemeral):

```text
/activity enable enabled:false
/activity list
/voice list
/activity inspect member:@Participant
```

Check guild management false, four voice rules with empty role lists, actual supported activity names/type/application IDs, unchanged voice destinations and no role mutations. If rolling forward after a previously failed attempt, the persisted guild flag may no longer default false: confirm it is false before starting another presence-enabled candidate under a separately reviewed retry plan. A fresh first migration seeds false; environment configuration cannot override this database gate.

## Block E — stage reviewed rules, then transfer authority in Discord

Use the following exact activity names and **native role option IDs**. `role:<@&ID>` below denotes the existing role mention/selection; Discord's slash UI requires selecting the role object (paste/search its ID in the role picker). It is not a free-text role-name lookup or a shell command. Quotes delimit names containing spaces and are not part of the stored activity name. All commands remain future manual actions during the approved window.

Stage the seven exported mappings while `/activity list` shows guild management disabled:

```text
/activity track name:TETR.IO type:Playing role:<@&1092394298139480146>
/activity track name:Sketchful.io type:Playing role:<@&1422351434501984268>
/activity track name:Minecraft type:Playing role:<@&1557000596647575582>
/activity track name:VALORANT type:Playing role:<@&1069147944227442708>
/activity track name:Gartic.io type:Playing role:<@&1069202917283868742>
/activity track name:Gartic type:Playing role:<@&1069579310022807672>
/activity track name:"Among Us" type:Playing role:<@&1073952995357753354>
/activity list
```

Omit application-ID restrictions for these name/type mappings: no IDs were exported and none should be guessed. Use `/activity edit id:<existing-rule-id> ...` if a mapping was already staged; repeating `track` for an existing name/type is rejected. Preserve `Gartic` → Playing Gartic Phone and `Among Us` → A Mong Goose exactly; do not substitute role names for activity names.

These are the exact fallback commands **if all four categories are approved enabled**:

```text
/activity fallback type:Playing enabled:true role:<@&1069145493852131329>
/activity fallback type:Listening enabled:true role:<@&1069145494674214973>
/activity fallback type:Watching enabled:true role:<@&1069145496054153236>
/activity fallback type:Streaming enabled:true role:<@&1069145496985284648>
/activity list
```

Playing/Other Games is intended enabled. **Listening/Watching/Streaming enable flags remain a deployment question:** retaining the roles does not establish the previous enable flags. If a category must remain disabled, use its exact command above with `enabled:false`; record the approved final set before execution. Never use `create-default` or `create-role` for these eleven existing roles. Staged enabled rules/fallbacks cannot assign roles until the guild management switch is enabled.

For the new dedicated Codenames role, first inspect a participant's real PreMiD activity. Then, with guild management still disabled, use the confirmed name and supported type:

```text
/activity inspect member:@Participant
/activity track name:"<exact reported Codenames activity name>" type:<reported type> create-role:"Playing Codenames"
/activity list
```

Only this new dedicated role uses `create-role`. The placeholders are intentional: the PreMiD name/type is still unknown, so an executable match cannot safely be filled in yet. Record the resulting role ID from configuration/Discord and verify Manage Roles/hierarchy. No Codenames application-ID restriction is needed unless independently verified and explicitly chosen. Keep the new role on rollback; do not automatically delete it or touch the community role.

**Manual transfer interlock:** in Discord Server Settings → Members, select **TuxedoMan `441366628836442122`**, remove **RoleBot `767344292284334090` and tuxbot `1324170106099597372` from that member only**. Keep all role objects, other member assignments and VC Roles unchanged. Reopen the member/role settings and verify no effective Administrator/Manage Roles remains (include @everyone and every remaining role). Let in-flight operations settle and confirm no further TuxedoMan role mutations. Do not edit the shared RoleBot permissions. If effective revocation cannot be established, do not enable Mr-Clean. This is reversible and works without host access.

Only after the interlock succeeds:

```text
/activity enable enabled:true confirm-cutover:true
/activity adopt role:@Other-Games confirm:true
/activity adopt role:@ReviewedEnabledCategoryRole confirm:true
/activity adopt role:@ReviewedDedicatedGameRole confirm:true
```

Repeat once per reviewed **distinct** enabled role: the seven verified named-role IDs above and each enabled generic role. A newly created Codenames role has no TuxedoMan grants to adopt; the reconciler owns grants it actually acquires. Do not bulk-adopt the community codenames role. For mixed/manual grants use `/activity adopt role:@Role member:@ReviewedMember confirm:true` for each reviewed human instead of whole-role adoption. Bulk adoption downloads the member cache, refuses incomplete data and excludes bots. Compare reported counts to reviewed eligible humans, not raw holder totals above. Adoption persists ownership/contribution IDs but does not directly mutate Discord. It does not wake an immediate sweep itself; role changes reconcile on fresh events and the existing 30-second retry sweep. Monitor convergence. No blanket confirm-empty or wholesale role replacement.

Then configure only the approved dedicated shared role:

```text
/voice edit channel:<Codenames source 1126902663607484476> add-role:@ReviewedPlayingCodenamesRole
```

Use the same ID as the presence rule. Never select Connected or the excluded 37-holder `codenames` community role. Select only the new dedicated Playing Codenames ID recorded above. Check DB status read-only:

```bash
docker exec -i mr-clean-db sh -c 'exec psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
BEGIN READ ONLY;
SELECT * FROM "ActivityGuildSettings";
SELECT * FROM "ActivityRules";
SELECT * FROM "ActivityFallbacks";
SELECT "RoleId", "Confirmed", count(*) FROM "VoiceRoleOwnerships" GROUP BY "RoleId", "Confirmed";
SELECT count(*) FROM "ActivityMemberStates";
SELECT "Destination", count(*) FROM "VoiceLogMessages" GROUP BY "Destination";
COMMIT;
SQL
```

## Block F — manual acceptance, then complete retirement

Use real consenting participants, no fabricated gateway events/test messages. Test tracked game/PreMiD start → stop; tracked ↔ untracked switching; simultaneous tracked/untracked games; Other Games removal after the last qualifying game; fresh offline → return; shared voice OR presence in both ending orders. Confirm unrelated roles remain. Unknown startup/downtime presence can retain grants; inspect/review rather than bulk-clear them. Verify all four voice routes, plain-text joins/embed-only departures, 120-second reconnects excluding gaps, interruption count including zero, Connected's restored Annoyinglog messages and continued VC Roles assignments. Observe ordinary avatar/moderation/role logging and outbox drainage; read-only startup checks alone do not prove those full live paths. Do not rename channels or restart for recovery testing without that included in approval.

Inspect stable running state/restarts, private logs and Block C/E queries. If accepted, **during the same approved session** complete TuxedoMan retirement by removing/kicking the TuxedoMan member/application from this server through Discord (not banning or deleting roles). Do this only after a complete mapping/settings export and the rollback restoration path are accepted; guild-data retention after removal is unknown. Verify existing non-managed generic/named roles and member grants remain. Bot integration/managed roles may be removed by Discord; preserve the unrelated role objects and reviewed custom role assignments. Do not remove VC Roles. If export/restoration or event-role scope is unresolved, final retirement is blocked; do not label the deployment complete or extend dual management as a workaround.

## Block G — rollback commands and Discord actions

A new shell can restore the variables without loading any secret env file:

```bash
set -euo pipefail
umask 077
export VOICE_CUTOVER_DIR=/home/tim/.local/share/mr-clean/deployments/presence-dacd084
export VOICE_ROLLBACK=mr-clean-rollback-bee7e73-presence-dacd084
```

If the new bot responds, first `/activity enable enabled:false`; remove only newly added shared voice bindings with `/voice edit channel:<source> remove-role:@TransferredRole`. Keep presence registrations enabled while paused; do not untrack/disable them during handback. Preserve ownership rows and member grants. If commands fail, stop the new process immediately instead of live SQL cleanup.

The following covers a candidate container created under `mr-clean-dev`; if candidate creation failed, there may be no such container. Preserve any failed candidate before restoring the original name:

```bash
if docker container inspect mr-clean-dev >/dev/null 2>&1; then
  # Guard: never archive an unexpected/current original container as the candidate.
  test "$(docker inspect --format '{{.Image}}' mr-clean-dev)" = "$(cat "$VOICE_CUTOVER_DIR/candidate-image.txt")"
  docker update --restart=no mr-clean-dev
  docker stop --time 60 mr-clean-dev
  docker logs mr-clean-dev > "$VOICE_CUTOVER_DIR/failed-candidate.log" 2>&1
  docker rename mr-clean-dev mr-clean-failed-presence-dacd084
fi
test "$(docker inspect --format '{{.Image}}' "$VOICE_ROLLBACK")" = "$(cat "$VOICE_CUTOVER_DIR/original-image.txt")"
test "$(docker inspect --format '{{.State.Running}}' "$VOICE_ROLLBACK")" = false
docker rename "$VOICE_ROLLBACK" mr-clean-dev
docker update --restart=unless-stopped mr-clean-dev
docker start mr-clean-dev
docker inspect --format 'state={{.State.Status}} restarts={{.RestartCount}} image={{.Image}}' mr-clean-dev
docker logs --tail 100 mr-clean-dev > "$VOICE_CUTOVER_DIR/rollback-startup.log" 2>&1
```

If startup failed before original rename completed, inspect names/state and start the original directly; do not run this block blindly. If the original image was accidentally pruned but its archive was preserved, `gzip -dc "$VOICE_CUTOVER_DIR/original-image.tar.gz" | docker image load` restores it; this does not restore a deleted container automatically. Do not delete the preserved container or substitute the older misleadingly named `mr-clean-prev-voice-bee7e73`.

**Discord handback only after candidate stopped:**

- Before permission revocation: TuxedoMan never stopped managing; leave it unchanged.
- After revocation, before removal: restore **RoleBot `767344292284334090` and tuxbot `1324170106099597372` to TuxedoMan `441366628836442122` only**. Verify effective Manage Roles and normal activity reconciliation. Do not change shared role definitions or VC Roles.
- After final removal: manually re-invite the same public app with no requested permissions via `https://discord.com/oauth2/authorize?client_id=441366628836442122&permissions=0&scope=bot&guild_id=683633975838769192`. Verify effective permissions remain withheld; restore exported mappings/settings using the confirmed installed interface/operator procedure, then restore original custom member roles/hierarchy (including RoleBot/tuxbot) when ready to resume. The public original invite requests Manage Roles, so do not use it for a gated re-add. If the configuration interface requires Manage Roles, restore authority only after the Mr-Clean candidate is stopped and account for TuxedoMan reconciliation during setup. Its private mapping retention is unverified: this manual restoration procedure must be accepted before removal is approved.

Retain all additive migrations, rules, ownership/contributions, pending voice messages and sessions. Never run Down migrations, mass-strip roles or restore the whole database merely to roll back the application. Original bee7e73 ignores new role-management tables and preserves its original voice logger; its old Connected suppression resumes. On any later retry, review the persisted enabled flag and ownership against fresh TuxedoMan grants before reconnecting the new reconciler. API status can prove permission loss, not private external service shutdown; once retired by removal the bot is absent from this guild regardless of hosting.

## Preparation validation

Seven bash blocks passed `bash -n` without execution; local documentation links and `git diff --check` pass. The route-dump table selectors were additionally validated with a schema-only read whose output was discarded. Deployment, image build/save, fresh backup creation, permission revocation, configuration commands and migrations were not executed. This handover changes documentation only; the implementation remains the previously tested dacd084 source (143 tests and successful Release build), so its test suite was not rerun for the run-sheet edits.
