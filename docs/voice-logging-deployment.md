# Controlled voice logging deployment

This is a plan, not an executable deployment script. Do not start a second bot instance using the same token/database. Keep the current bot running until a deployment is explicitly authorized.

## Preparation without service changes

1. Confirm the branch and full tests. Check the exact four source IDs, labels and three destinations in [appsettings.json](../MrClean/appsettings.json). Leave `VoiceLogging:Enabled=false` in Git.
2. Recheck source View Channel access and destination View Channel, Send Messages and Read Message History access with the deployed bot identity. Read-only checks cannot prove successful message delivery; that is part of the live test. Preserve VC Roles and existing role-log routes.
3. Record the current image ID, container configuration, environment, network, restart policy, volumes, resource limits and stop timeout in a private deployment directory outside the repository. Treat container inspection output as a secret: it contains bot/database credentials. Preserve the current container and image for rollback. Do not commit machine-specific settings or backups.
4. Confirm PostgreSQL connectivity, current migration history, and the bot database user's schema USAGE/CREATE and migration-history SELECT/INSERT privileges. Pending migration `20261017120000_AddVoiceActivityState` only adds `VoiceActivityStates`, `VoiceLogMessages` and an index; it does not alter existing feature tables.
5. Take a PostgreSQL custom-format backup using the established backup mechanism into private storage outside Git; verify it with `pg_restore --list` and check available disk space. This can run without stopping PostgreSQL. An archive listing verifies readability, not a successful restore rehearsal. Keep the backup in approved durable private storage before migration.
6. Build a uniquely tagged image from the repository root with `docker build -t <candidate-tag> -f MrClean/Dockerfile .`. The root `.dockerignore` excludes `.env` files, local production appsettings and Git metadata. Do not use secret build arguments. Do not start the image against production merely to inspect it: the bot automatically migrates the database on startup, even when voice logging is disabled.
7. Preserve all existing runtime environment values, especially the token, connection string and `Discord__GuildId=683633975838769192`. If using the Development environment, keep that explicit guild override because Development appsettings target another guild. Use a private environment file with mode 0600, never command-line token/password literals or a tracked file. Confirm no old `VoiceLogging__...` overrides conflict with the committed mapping.

## Authorized cutover and controlled test

1. Confirm the backup, rollback image/container, configuration and test participant are ready. Add `VoiceLogging__Enabled=true` only to the candidate's private runtime configuration. Keep the reconnect window at 120 seconds and preserve the supplied ID mappings.
2. Gracefully stop the old bot and retain its container under an unused rollback name. Keep its original restart policy/configuration recorded; ensure it cannot run concurrently with the candidate. Do not stop, replace, recreate or remove PostgreSQL or its volume.
3. Start one candidate with the old bot's runtime settings/network and sufficient graceful shutdown time. Use the new image tag and the private enable override. The additive migration runs automatically before Discord login. Retain the old image and stopped container.
4. Confirm the process remains running, migration completes, Discord connects to the intended guild, and avatar initialization/moderation/role logging still work. Check for database, permissions and outbox errors. Existing occupants of tracked channels produce observation-time joins; this is expected.
5. With a participant, verify each of the four voice channels routes to its configured destination. Test a brief disconnect and reconnect under 120 seconds: logs should be join, departure, rejoin, then final departure, with disconnected time excluded. Test a reconnect beyond 120 seconds and a channel switch. Verify departure-before-arrival generation and chronology within each destination. Different destinations do not share delivery ordering.
6. Test a general Voice rename only with explicit permission from the server operator; the logger itself never renames channels. Confirm a later departure uses the then-current name while the earlier join stays unchanged. Fixed Music/Gartic Phone/Codenames labels must remain fixed.
7. Inspect the outbox using read-only queries: `SELECT "Destination", count(*), min("Id") FROM "VoiceLogMessages" GROUP BY "Destination";`. Check that it drains and that `[vc:N]` references do not repeat unexpectedly. Read-only inspection must not delete queued logs or modify session state.
8. A planned candidate restart to verify persisted recovery requires separate authorization if it was not included in the deployment approval. Recovery must label uncertainty and exclude unobserved time. Do not restart PostgreSQL to test this feature.

## Rollback

1. Gracefully stop the candidate and retain its logs/container for investigation. Ensure it is stopped before starting the old bot.
2. Restore the old container's name/restart policy if changed and start it using the recorded original image and environment. Do not replace its configuration with the candidate's settings. Confirm Discord connectivity and existing features.
3. Leave the additive voice tables and migration-history entry in place. The old application ignores them. **Do not downgrade the database or restore the whole backup for an application rollback**: that would discard subsequent moderation/configuration data.
4. Preserve pending voice messages and sessions. Decide how to handle queued historical logs before a future re-enable; they retain their original destinations and can be delivered then. Do not silently purge them. Recovery gaps will be labelled.
5. A database restore is a separate recovery operation only if the database itself is damaged, with explicit authorization and a plan to preserve changes made since the backup.

## Limits and decision gate

Use one bot instance per database/guild. Duplicate suppression searches only the latest 100 messages. A crash can lose events awaiting persistence; prolonged database downtime can grow the in-memory queue. The code and unit tests do not substitute for the controlled Discord delivery/recovery exercise.

Proceed only after the channel/permission checks, full tests, candidate image build and verified database backup pass, and the operator authorizes the cutover. No deployment, enablement or restart is implied by completing preparation.
