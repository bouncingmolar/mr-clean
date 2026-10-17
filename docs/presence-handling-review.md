# Presence termination review

Reviewed against Discord.Net **3.18.0**, the project's installed dependency. No library was replaced or added. No deployment, live database/configuration changes or Discord events were performed for this review.

## Cause and supported contract

[Discord's Presence Update fields](https://docs.discord.com/developers/events/gateway-events#presence-update) specify `activities` as the user's current activities. It is not marked optional. The adjacent partial-data warning concerns the nested user object.

[Discord.Net's dispatcher](https://github.com/discord-net/Discord.Net/blob/3.18.0/src/Discord.Net.WebSocket/DiscordSocketClient.EventHandling.cs), in `PRESENCE_UPDATE`, clones the previous presence, updates the cached user, and then invokes `PresenceUpdated(user, before, after)`. Cache initialization/member downloads are not fresh PresenceUpdated callbacks. The application copies activities synchronously and performs no database/role calls on this callback. Configured-guild filtering and a comparison with current SDK activities reject foreign-guild or already superseded notifications.

[SocketPresence.Update/ConvertActivitiesList](https://github.com/discord-net/Discord.Net/blob/3.18.0/src/Discord.Net.WebSocket/Entities/Users/SocketPresence.cs) converts both a null model list and a zero-length list into an empty public collection. Thus its public interface cannot distinguish a raw omitted field from `[]`. Before/after snapshots do not restore that lost field-presence information. This is a library representation limitation, not a documented gateway requirement to treat all empty updates as unknown. A malformed/undocumented partial gateway update omitting activities cannot be distinguished using these APIs; this release relies on Discord's documented contract. Raw gateway interception is not warranted by the current evidence.

The actual Mr-Clean defect was an application heuristic requiring two fresh online empty frames separated by 30 seconds, plus active-client evidence. A normal termination often sends only one empty frame. Offline was also incorrectly treated as unknown. Both gates were removed. There is no activity expiry timer.

## TuxedoMan and Eris

[TuxedoMan's lockfile](https://github.com/alex-taxiera/TuxedoMan/blob/master/package-lock.json) resolves `@alex-taxiera/eris` **0.0.2**. The published [package archive](https://registry.npmjs.org/@alex-taxiera/eris/-/eris-0.0.2.tgz) was inspected without installing/executing it. In `lib/structures/Member.js`, `update` only replaces activities when the field is supplied; a supplied `[]` replaces the old list, while an omitted field retains it. In `lib/gateway/Shard.js`, PRESENCE_UPDATE saves old activities, updates the member, and emits the event. The repository's current default branch differs from this locked package and must not be substituted for it.

[TuxedoMan's handler](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/events/presenceUpdate.ts) compares the selected old/new activity names. Its [activity selector](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/modules/util/activity.ts) chooses one supported activity, or undefined for an empty list. Its [game manager](https://github.com/alex-taxiera/TuxedoMan/blob/master/src/modules/game-manager/game-manager.ts) serializes per member, then removes managed roles other than the selected explicit/generic role. A selected activity disappearing therefore promptly removes its role; recently joined members have a separate five-minute delay. It evaluates only one activity and name changes, and replaces the role collection. Mr-Clean instead evaluates all supported activities and changes individual roles through the shared reconciler.

## Mr-Clean transitions

| Observation | Result with presence management enabled |
| --- | --- |
| Tracked activity starts | Add its role if absent and manageable; leave existing unrelated grants unowned. |
| Game closes or PreMiD disappears | Fresh list omits that activity: remove its owned role unless another activity/voice trigger requires it. |
| Tracked game switches to an untracked game | Release explicit contribution and acquire enabled Playing fallback. |
| Multiple simultaneous activities | Evaluate each independently; union resulting roles. |
| Last qualifying untracked activity ends | Release generic contribution immediately. |
| Fresh offline update with empty list | Confirm no reported activity; release presence contribution. Empty active-client information does not block it. |
| Member returns with activity | Fresh positive update re-establishes its contribution. |
| Bot disconnects/guild unavailable | Invalidate observations; retain possible durable contributions. |
| Startup/reconnect cached empty or offline member | Unknown, not confirmed inactivity. Positive cache can seed incomplete evidence. |
| Fresh empty update after reconnect | Clear retained presence contribution immediately. |

“Immediately” means an event-driven reconciliation wake, with no intentional inactivity wait; database, earlier queued members, Discord rate limits and role API latency still apply. Per-member SemaphoreSlim locks serialize mutations. Reconciliation reads latest cached membership and presence inside the lock; later updates wake another pass, so a transition during an in-flight request converges afterward. Source revisions reject older explicit cache observations, and duplicate events avoid redundant API calls. SDK before/after activity name comparison is not used as a termination detector: identical-name type/application changes still matter.

Voice OR presence uses one ownership ledger. Either trigger may end first; only absence of both permits removal. Confirmed grants acquired/adopted by Mr-Clean are removable. Existing manual/TuxedoMan grants are not implicitly adopted. Explicit adoption requires management enabled and a reviewed configured role. Pending ambiguous grants are relinquished rather than stripped.

With management disabled (or capture disabled), enabled registered presence roles are frozen against both presence and overlapping voice mutations; existing saved contributions are protected while present. Voice-only roles continue normally. Keep shared bindings empty until cutover, and avoid unregistering those protected roles while another bot manages them.

## Remaining limits and deployment prerequisites

Roles may remain stale if the activity ended while the bot was offline and Discord sends no fresh update afterward, the guild/member cache is unavailable, role management is paused, permissions/hierarchy prevent changes, or an existing grant was never adopted. Unknown state has no arbitrary expiry. Reconciliation retries permission/API failures at the configured modest interval; REST cannot retrieve presence history. An administrator can inspect and deliberately confirm empty state when independently verified.

Invisible users are observable only as Discord reports them. Mr-Clean cannot recover hidden activity or distinguish invisibility from offline; a fresh reported empty list represents no reported activity, not a claim about the user's real-world activity. Discord's omitted-field information cannot be recovered from SocketPresence if the documented contract is violated.

An observation-only release needs approved migrations already documented in [activity-roles.md](activity-roles.md), Presence Intent in the Developer Portal, existing Server Members/Message Content intents, `ActivityRoles__ObservePresences=true`, and **guild management left false**. No new migration is added by this fix. The four voice seeds, duration accounting, output format, outbox, Connected protection and role-log routing are unchanged. The existing role-change logger alone records actual role mutations; Other Games routes to Annoyinglog, and the pending voice-rule migration restores Connected routing. Test real PreMiD transitions privately before approving TuxedoMan cutover. No running service was modified.

## Validation

The full test suite passes **143 tests**, with no failures or skips (previous baseline: 130). Regressions exercise first-empty termination through the production capture boundary for online/idle/DND/offline/invisible states without active clients, tracked-to-untracked switching, PreMiD disappearance, simultaneous activities, last fallback removal, both voice/presence ending orders, unknown restart/reconnect state, return after offline, stale cache seeding, an ending during an in-flight grant, permission recovery, explicit adoption, and observation-only overlapping bindings. Existing voice output/accounting, migration preservation, Connected routing, avatar, moderation and retention tests also pass.

Release build succeeds with **0 warnings and 0 errors**. Validation uses the .NET 6 SDK and isolated temporary source/build directories; migration tests generate SQL/model differences without connecting to a live database. Gateway and role operations are simulated in unit tests; real Discord delivery, Portal intent approval and PreMiD names remain controlled-deployment checks.
