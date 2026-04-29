# Day 7 Hardening & UAT Checklist

## 1) Hardening completed

### Idempotency and safe command behavior
- Duplicate in-flight command (`PENDING`/`SENT`) for same `pcId` + `type` now returns existing command.
- `OPEN` when active session already exists is rejected as immediate `ACK_FAILED` (no duplicate session).
- `LOCK` when no active session is treated as safe no-op `ACK_SUCCESS` and machine status is normalized to `LOCKED`.

### Session reliability
- Cron job every minute auto-closes `ACTIVE` sessions when machine remains `OFFLINE` beyond timeout.
- Timeout config: `SESSION_OFFLINE_CLOSE_MINUTES` (default 3).
- Auto-close uses `closedReason=AUTO_OFFLINE` and computes `durationSeconds`, `billableMinutes`, `amount`.

### Audit log baseline
- Command lifecycle events written to `events_log`.
- Presence transitions and auto-offline session close events written to `events_log`.
- Audit logging failures do not break request flows.

## 2) New/updated config

In backend `.env`:

- `AGENT_OFFLINE_TIMEOUT_SECONDS=30`
- `COMMAND_ACK_TIMEOUT_SECONDS=20`
- `SESSION_OFFLINE_CLOSE_MINUTES=3`

## 3) LAN UAT scenarios

### Scenario A: Presence and reconnect
1. Start backend.
2. Start one client agent (`agentId=PC-001`).
3. Verify admin `/pcs` shows `ONLINE` quickly.
4. Kill client process for > `AGENT_OFFLINE_TIMEOUT_SECONDS`.
5. Verify admin shows `OFFLINE`.
6. Start client again, verify status returns `ONLINE`.

Expected:
- No duplicate PC record.
- `events_log` has `pc.status.changed` transitions.

### Scenario B: Open/Lock normal flow
1. On admin `/pcs`, click `Open` for one machine.
2. Wait for command ack success.
3. Verify machine status becomes `IN_USE`.
4. Wait 2-3 minutes.
5. Click `Lock`.
6. Verify command ack success and status becomes `LOCKED`.

Expected:
- Exactly one active session created during open.
- Session closed during lock with billable minutes and amount.

### Scenario C: Double-click protection
1. Rapidly click `Open` multiple times.
2. Rapidly click `Lock` multiple times.

Expected:
- No duplicate active sessions.
- Duplicate in-flight commands are deduplicated/idempotent.

### Scenario D: Auto-close offline session
1. Open machine and keep session active.
2. Kill client/network so machine goes `OFFLINE`.
3. Wait longer than `SESSION_OFFLINE_CLOSE_MINUTES`.
4. Open `/history` page.

Expected:
- Session is auto-closed with `closedReason=AUTO_OFFLINE`.
- Revenue calculation includes that closed session.

## 4) Quick SQL checks (optional)

```sql
-- Active sessions should be <= 1 per machine
SELECT pc_id, COUNT(*)
FROM sessions
WHERE status = 'ACTIVE'
GROUP BY pc_id
HAVING COUNT(*) > 1;

-- Recent command events
SELECT created_at, event_type, payload
FROM events_log
WHERE event_type LIKE 'command.%'
ORDER BY created_at DESC
LIMIT 50;

-- Recent auto-offline closures
SELECT id, pc_id, started_at, ended_at, closed_reason, amount
FROM sessions
WHERE closed_reason = 'AUTO_OFFLINE'
ORDER BY ended_at DESC
LIMIT 20;
```
