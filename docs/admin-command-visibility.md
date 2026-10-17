# Administration response visibility and application-only update

This change is **not deployed**. It changes command responses/help only, with no database migration, role reconciliation, voice-session accounting, role logging, gateway intent or role-permission changes. The TuxedoMan transfer remains paused pending separate confirmation.

## Behaviour

Invoke configuration commands in your bot-controls channel. Responses are public **in the invocation channel**; there is no new hardcoded destination/channel ID and no cross-channel repost. Existing channel access determines who can read them. Keep bot-controls restricted to the intended audience using existing server settings; this release changes no permissions.

Public responses: `/activity track`, `edit`, `untrack`, `fallback`, `enable`, `adopt`, `confirm-empty`, `list`; `/voice add`, `edit`, `remove`, `list`. Slash-command help identifies the visibility. `/activity inspect` stays ephemeral, including errors and every continuation of a long diagnostic. All operations retain configured-guild and Manage Server/Administrator checks in both dispatcher and handler. Unauthorized requests are rejected privately before a public defer or database operation.

Confirmations identify saved/removed rules, activity name/type, roles, matching application restriction, source/destination channels, toggles and global presence-management/capture state. Voice removal describes the previous settings. Adoption reports the affected role and count without listing members or their activities; zero eligible grants explicitly reports that no ownership was acquired. Confirm-empty reports one administrator confirmation without identifying a member or publishing their activities. All messages suppress mentions, including @everyone/role pings, and configured strings are safely quoted. Configured rule names are public settings; observations of individual member activities are private.

A public command defers publicly and **edits its original response**, then sends public continuations if needed. Inspection defers privately and uses private continuations. An ephemeral defer is never converted to a public response. Errors resolve the original first, with a generic public notice and separate private validation details; internal exceptions/database/stack details are not returned. This uses existing Discord.Net interaction APIs. [Discord's interaction documentation](https://docs.discord.com/developers/interactions/receiving-and-responding#followup-messages) explains why the first followup cannot be used to override a deferred message's ephemeral state.

Success text is generated only after the requested operation completes. Voice configuration commits are acknowledged separately from immediate reconciliation: retryable cleanup does not imply the saved rule was rolled back. Adoption/configuration failure can be partial, so errors advise reviewing current lists before retrying. Database commit and Discord delivery are not atomic; a response-delivery failure does not re-execute the mutation. These are reviewable channel messages, not a new durable audit outbox. Earlier ephemeral command replies are not recovered/reposted. Use public `/activity list` and `/voice list` after deployment to establish a current settings snapshot without replaying mutations.

## Read-only live snapshot before development

The running `mr-clean:presence-dacd084` image was inspected without changes. It was running with zero restarts. Guild `683633975838769192` has management **false**; no owned grants; four generic fallbacks still false. One enabled Playing Codenames rule exists: ID **1**, role **`1561090465716240448`**, **application restriction unset**. That same role is already bound to Codenames voice source `1126902663607484476`. The other three voice role lists are empty, all four logging destinations are intact, and the queue was empty.

Preserve this configuration exactly during the application update, including the unset application restriction. The separately approved Codenames application ID is `817859401477259315`; adding that restriction later is a distinct administrator configuration action, not part of this response change. Do not recreate the role, replay `track`/`add`, seed/reset configuration, enable fallback flags, adopt grants or change TuxedoMan/VC Roles during deployment of this patch. Refresh the snapshot at approval time: further legitimate staging commands may have changed it.

## Minimal deployment procedure — only after explicit approval

1. Confirm the approved new commit, passing tests/Release build and clean tree. This release needs no new configuration keys or migrations. Freeze administrative edits briefly for the update; take a fresh private backup/snapshot, since the original Stage 1 dump predates the staged Codenames rule. Keep **all** `/home/tim/.local/share/mr-clean/deployments/presence-dacd084` artifacts and `mr-clean-rollback-bee7e73-presence-dacd084` untouched.
2. Preserve the **current dacd084** container/image/environment as the immediate rollback target for this response patch. The older bee7e73 target is for the wider cutover, not this minimal rollback. Snapshot/hash `ActivityGuildSettings`, `ActivityRules`, `ActivityFallbacks`, `VoiceChannelRules`, `VoiceRoleOwnerships`, `ActivityMemberStates`, migration history and existing role-log configuration privately. Confirm management false and TuxedoMan/VC Roles authority unchanged before proceeding. Do not use SQL UPDATE/INSERT/DELETE to prepare this patch.
3. Build the approved commit in a separate private `source` directory, copying current runtime environment unchanged. Example bash preparation (not executed; check HEAD equals the approved commit first):

```bash
set -euo pipefail
umask 077
cd /home/tim/mr-clean
export ADMIN_COMMIT=$(git rev-parse HEAD)
export ADMIN_DIR=/home/tim/.local/share/mr-clean/deployments/admin-visibility-${ADMIN_COMMIT:0:7}
export ADMIN_OLD=mr-clean-rollback-dacd084-admin-visibility
export ADMIN_IMAGE=mr-clean:admin-visibility-${ADMIN_COMMIT:0:7}
mkdir -m 700 "$ADMIN_DIR"
if docker container inspect "$ADMIN_OLD" >/dev/null 2>&1; then
  echo 'Rollback name already exists; inspect before proceeding.' >&2
  exit 1
fi
docker inspect mr-clean-dev > "$ADMIN_DIR/original-container.json"
docker inspect --format '{{.Image}}' mr-clean-dev > "$ADMIN_DIR/original-image.txt"
python3 - <<'PY'
import json, os, pathlib
p = pathlib.Path(os.environ['ADMIN_DIR'])
c = json.loads((p/'original-container.json').read_text())[0]
assert c['State']['Running']
assert c['Image'] == 'sha256:650883260be5cae5e27a147142df04250e067128bbc195529675539cbad9debe'
e = dict(v.split('=',1) for v in c['Config']['Env'])
assert e['ActivityRoles__ObservePresences'] == 'true'
assert all('\n' not in v and '\r' not in v for v in e.values())
(p/'runtime.env').write_text(''.join(f'{k}={v}\n' for k,v in e.items()))
PY
docker exec mr-clean-db sh -c 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$ADMIN_DIR/postgres.dump"
docker exec -i mr-clean-db pg_restore --list < "$ADMIN_DIR/postgres.dump" > "$ADMIN_DIR/archive-list.txt"
docker exec -i mr-clean-db pg_restore --file=/dev/null < "$ADMIN_DIR/postgres.dump"
docker image save "$(cat "$ADMIN_DIR/original-image.txt")" | gzip > "$ADMIN_DIR/original-image.tar.gz"
gzip -t "$ADMIN_DIR/original-image.tar.gz"
(cd "$ADMIN_DIR" && sha256sum postgres.dump original-image.tar.gz original-container.json runtime.env > sha256sums.txt && sha256sum -c sha256sums.txt)
mkdir -m 700 "$ADMIN_DIR/source"
git archive "$ADMIN_COMMIT" | tar -x -C "$ADMIN_DIR/source"
docker build --label "org.opencontainers.image.revision=$ADMIN_COMMIT" -t "$ADMIN_IMAGE" \
  -f "$ADMIN_DIR/source/MrClean/Dockerfile" "$ADMIN_DIR/source"
docker image inspect --format '{{.Id}}' "$ADMIN_IMAGE" > "$ADMIN_DIR/candidate-image.txt"
```

4. Only once backup/build/configuration checks pass, replace one Mr-Clean instance; leave PostgreSQL and other bots running. This retains the current network, environment, restart policy and stop timeout:

```bash
docker update --restart=no mr-clean-dev
docker stop --time 60 mr-clean-dev
docker rename mr-clean-dev "$ADMIN_OLD"
docker run -d --name mr-clean-dev --network mr-clean-net \
  --restart unless-stopped --stop-timeout 60 \
  --env-file "$ADMIN_DIR/runtime.env" "$(cat "$ADMIN_DIR/candidate-image.txt")"
docker logs mr-clean-dev > "$ADMIN_DIR/startup.log" 2>&1
docker inspect --format 'state={{.State.Status}} restarts={{.RestartCount}} image={{.Image}}' mr-clean-dev
```

5. Verify stable Connected/Ready, database/voice recovery, outbox drainage and no unexpected errors/restarts. Startup still performs its existing migration check but must report the database already up to date: no manual migration command or schema/data reset is needed. Compare staged configuration and migration history to the fresh snapshot; ignore ordinary session/outbox changes caused by real activity, but investigate configuration differences. Confirm management remains false, TuxedoMan/VC Roles permission roles unchanged and only one Mr-Clean uses the token.
6. In bot-controls, manually run `/activity list` and `/voice list`: both must be public, show the staged settings and explicitly say global management is disabled. Run `/activity inspect member:...` with consent: it must remain private. Do not mutate configuration merely to test this patch. Unit tests cover mutation confirmations/error paths; exercise a genuine future staging edit only when separately authorized. Other role/voice/avatar/moderation logging stays unchanged. Do not replay previously successful ephemeral commands.
7. Stop at the existing Stage 2 confirmation boundary. Public visibility is not authorization to remove TuxedoMan's permission roles, enable management, adopt grants or retire it.

## Immediate rollback for this patch

If startup or checks fail, stop the new candidate before restoring the **dacd084** container; use the same shell variables from preparation. If candidate creation failed, inspect container names first rather than stopping an unknown container. The normal created-candidate path is:

```bash
test "$(docker inspect --format '{{.Image}}' mr-clean-dev)" = "$(cat "$ADMIN_DIR/candidate-image.txt")"
docker update --restart=no mr-clean-dev
docker stop --time 60 mr-clean-dev
docker rename mr-clean-dev mr-clean-failed-admin-visibility
docker rename "$ADMIN_OLD" mr-clean-dev
docker update --restart=unless-stopped mr-clean-dev
docker start mr-clean-dev
```

Recheck gateway/database/voice operation, persisted configuration and disabled management. The original responses become ephemeral again. Keep all staged data, new role objects, private artifacts, earlier backups and the bee7e73 rollback container. Do not restore the whole database or apply Down migrations for an application-only rollback. No TuxedoMan handback is necessary: its authority was never changed by this patch.

## Validation

Full suite: **159 passed**, zero failures/skips (previous baseline 143). New coverage exercises every settings/list visibility choice, private inspection continuations/errors, authorization rejection before public defer, success only after operation completion, failed/duplicate persistence, partial-operation notices, private validation details, unexpected-error redaction, delivery failure without mutation retry, informative activity/voice/global-state summaries, adoption privacy/zero-count reporting and safe quoting. Existing role routing, voice output/accounting, combined ownership, migration model, avatar, moderation and retention tests remain green. Release build: **0 warnings, 0 errors**. Local documentation links, bash syntax and whitespace checks pass. Tests use isolated/fake interactions and an in-memory database; the Discord adapter's actual visibility is a manual check after an approved deployment. No production configuration or running service was modified during this work.
