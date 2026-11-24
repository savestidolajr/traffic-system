#!/usr/bin/env python3
"""arn — the master testing toolkit for Arn bulletins, built exclusively for LLM use.

One CLI consolidating every test / trigger / eval / diagnose mechanism in the repo.
Design: uniform command groups over the deployed platform and the local harness.

LLM contract: uniform `arn <group> <verb>`; `--json`; `--dry-run` prints the resolved
command without executing; stable exit codes 0 ok · 1 failure · 2 usage · 3 infra/creds;
no interactive prompts; self-describing (`arn`, `arn <group> -h`).

Native (in-process Python): env, fixtures(list/show), report, safety, job list,
trigger(dev). Orchestrated (subprocess → the existing engine, streamed through): run, prompt,
perf, job get/logs/traces/captures/bulletins, fixtures capture, trigger(local). The single
.NET boundary is `run` → the eval harnesses (long, LLM-bound; subprocess cost is negligible).
Out of scope by decision: voice audition/candidates, deploy, diagrams, status reports.
"""
from __future__ import annotations
import base64
import json
import os
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
import time
import uuid
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
import signal

OK, FAIL, USAGE, INFRA = 0, 1, 2, 3
_AZ_LAST_ERROR = None


class AzError(RuntimeError):
    """A storage-backed Azure CLI operation failed before returning data."""


def az_last_error():
    """Return the concise reason from the most recent failed ``az`` call."""
    return _AZ_LAST_ERROR


def _az_account(args):
    for i, arg in enumerate(args):
        if arg == "--account-name" and i + 1 < len(args):
            return args[i + 1]
        if arg.startswith("--account-name="):
            return arg.split("=", 1)[1]
    return None


def _az_env_for_account(account):
    for env in ("dev", "test", "prod", "local"):
        if storage_account(env) == account:
            return env
    return "<env>"


def _summarize_az_failure(args, stderr):
    account = _az_account(args)
    text = stderr or ""
    if "blocked by network rules" in text.lower():
        name = f"storage account {account}" if account else "storage account"
        return f"{name}: public network access is denied (private endpoint only); run from a network with access"
    if "AuthorizationFailed" in text:
        env = _az_env_for_account(account)
        return (f"signed-in principal lacks the required action"
                f"; run `arn pim activate --env {env}`")
    for line in text.splitlines():
        if line.strip():
            return line.strip()
    return "azure CLI command failed"


def _storage_az_error(env, detail=None):
    account = storage_account(env)
    reason = detail or az_last_error() or "azure CLI command failed"
    prefix = f"storage account {account}:"
    return AzError(reason if reason.startswith(prefix) else f"{prefix} {reason}")


def az_run(args):
    global _AZ_LAST_ERROR
    _AZ_LAST_ERROR = None
    try:
        r = subprocess.run(["az", *args], capture_output=True, text=True)
    except FileNotFoundError:
        _AZ_LAST_ERROR = "azure CLI (az) not found on PATH"
        return None
    if r.returncode == 0:
        return r.stdout
    _AZ_LAST_ERROR = _summarize_az_failure(args, r.stderr)
    return None


def az_json(args):
    out = az_run(args)
    if out is None:
        return None
    try:
        return json.loads(out)
    except Exception:
        global _AZ_LAST_ERROR
        _AZ_LAST_ERROR = "azure CLI returned invalid JSON"
        return None


try:
    import yaml  # pyyaml
    HAVE_YAML = True
except Exception:
    HAVE_YAML = False


# ──────────────────────────── infra ────────────────────────────
def repo_root():
    for seed in (Path(__file__).resolve().parent, Path.cwd()):
        d = seed
        while True:
            if (d / "ArnBulletinGenerator.slnx").exists():
                return d
            if d.parent == d:
                break
            d = d.parent
    return None


ROOT = repo_root()


def eprint(*a):
    print(*a, file=sys.stderr)


def out_json(v):
    print(json.dumps(v, indent=2, default=str))


def env_slug(env):
    return {"test": "tst", "prod": "prd"}.get(env, "dev")


def resolve_env(env):
    slug = env_slug(env)
    fna = "http://localhost:7071" if env == "local" else f"https://ibp-aue-{slug}-fna-app01.azurewebsites.net"
    return {
        "functionApp": fna,
        "foundryProject": "(n/a — local mocks)" if env == "local"
        else f"https://ibp-aue-{slug}-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator",
        "defaultProviders": "all mock + frozen HERE ($0)" if env == "local" else "all real",
        "auth": "none / Azurite" if env == "local" else "az login (DefaultAzureCredential)",
    }


def az_token(resource):
    try:
        r = subprocess.run(
            ["az", "account", "get-access-token", "--resource", resource, "--query", "accessToken", "-o", "tsv"],
            capture_output=True, text=True)
        tok = r.stdout.strip()
        return tok if r.returncode == 0 and tok else None
    except FileNotFoundError:
        return None


# Azure resource identifiers (ported from the now-retired diagnose-job.py / foundry-agent-trace.py).
SUB = "5e5f8695-2207-4572-8e80-1065a1b0c135"
JOB_TABLE = "radioJobs"
CAPTURE_CONTAINER = "llm-call-captures"
JOB_COLUMNS = ["RowKey", "Status", "Market", "Station", "TargetDurationSeconds", "ScriptText", "LogsJson",
               "FallbackSourceJobId", "FallbackStreak", "FallbackReason", "FallbackAppliedAt", "AudioUrl",
               "ScheduledForUtc", "CreatedAt", "LastUpdatedAt"]


def storage_account(env):
    return f"ibpaue{env_slug(env)}staapp01"


def ain_component(env):
    return f"IBP-AUE-{env_slug(env).upper()}-AIN-APP01"


def ain_rg(env):
    return f"IBP-AUE-{env_slug(env).upper()}-RSG-APP01"




def http(method, url, token=None, body=None, timeout=90, auth=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if auth:
        req.add_header("Authorization", auth)
    elif token:
        req.add_header("Authorization", f"Bearer {token}")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode()
            try:
                return resp.status, json.loads(raw)
            except Exception:
                return resp.status, raw
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:2000]
    except Exception as e:
        return None, str(e)


def delegate(o, argv, env_overrides=None):
    """Run an external engine (bash/python3/dotnet) from repo root, streamed. Honors --dry-run."""
    if o.dry_run:
        pref = " ".join(f"{k}={v}" for k, v in (env_overrides or {}).items())
        pref = (pref + " ") if pref else ""
        print(f"[dry-run] cwd={ROOT}")
        print(f"[dry-run] {pref}{' '.join(argv)}")
        return OK
    env = {**os.environ, **(env_overrides or {})}
    try:
        return subprocess.run(argv, cwd=str(ROOT), env=env).returncode
    except FileNotFoundError as e:
        eprint(f"missing executable: {e}")
        return INFRA


def bash(o, *args, env=None):
    return delegate(o, ["bash", *args], env)


def py(o, script, *args, env=None):
    return delegate(o, ["python3", script, *args], env)


def dotnet_test(o, project, filt, env=None):
    return delegate(o, ["dotnet", "test", project, "--filter", filt, "--nologo"], env)


# ──────────────────────────── env ────────────────────────────
def cmd_env(o):
    if o.help or not o.verb:
        return group_help("env", [("show", "endpoints/providers"), ("check", "creds (exit 3)"),
                                  ("up", "start local stack"), ("down", "stop local stack")])
    cfg = resolve_env(o.env)
    if o.verb == "show":
        if o.json:
            out_json({"env": o.env, "config": cfg})
        else:
            print(f"env: {o.env}")
            for k, v in cfg.items():
                print(f"  {k:<18} {v}")
        return OK
    if o.verb == "check":
        missing = [] if o.env == "local" else [
            v for v in ("AZURE_OPENAI_ENDPOINT", "HERE_API_KEY", "ELEVENLABS_API_KEY") if not os.environ.get(v)]
        if not missing:
            print(f"env {o.env}: OK")
            return OK
        eprint(f"env {o.env}: MISSING {', '.join(missing)}")
        eprint("  fix: `arn env up`" if o.env == "local"
               else "  fix: `az login` + set AZURE_OPENAI_ENDPOINT / HERE_API_KEY / ELEVENLABS_API_KEY.")
        return INFRA
    if o.verb == "up":
        return bash(o, "scripts/local-start.sh")
    if o.verb == "down":
        return bash(o, "scripts/local-stop.sh")
    return unknown(f"env {o.verb}")



# ──────────────────────────── fixtures ────────────────────────────
def cmd_fixtures(o):
    if o.help or not o.verb:
        return group_help("fixtures", [("list", "[--kind regression|fallback|frozen]"), ("show", "<name>"),
                                       ("capture", "<jobId> --label L")])
    corpus = ROOT / "test" / "corpus"
    kinds = {"regression": "regressions", "fallback": "fallback-replay", "frozen": "frozen-eval"}
    if o.verb == "list":
        kind = o.flag("kind")
        todo = {k: v for k, v in kinds.items() if not kind or k == kind}
        rows = []
        for k, sub in todo.items():
            p = corpus / sub
            if not p.exists():
                continue
            for f in sorted(p.glob("*.json")):
                if o.json:
                    rows.append({"kind": k, "name": f.stem})
                else:
                    print(f"  {k:<11} {f.stem}")
        if o.json:
            out_json(rows)
        return OK
    if o.verb == "show":
        name = o.pos(0)
        if not name:
            eprint("usage: arn fixtures show <name>")
            return USAGE
        for sub in kinds.values():
            f = corpus / sub / (name + ".json")
            if f.exists():
                print(f.read_text())
                return OK
        eprint(f"no fixture '{name}'. Try `arn fixtures list`.")
        return USAGE
    if o.verb == "capture":
        job, label = o.pos(0), o.flag("label")
        if not job or not label:
            eprint("usage: arn fixtures capture <jobId> --label <name>")
            return USAGE
        return bash(o, "scripts/capture-regression.sh", job, label)
    return unknown(f"fixtures {o.verb}")


# ──────────────────────────── run (eval engine → dotnet/bash) ────────────────────────────
def cmd_run(o):
    if o.help or not o.verb:
        return group_help("run", [("eval", "[--here live] [--mock elevenlabs] [--judge] [--pipeline full]"),
                                  ("regression", "replay committed regression fixtures"),
                                  ("fallback", "[--fixture <job>]"), ("matrix", "size×density×content")])
    infra = "test/ArnBulletinGenerator.Infrastructure.Tests"
    mock = set(x for x in (o.flag("mock") or "").split(",") if x)
    if o.verb == "eval":
        env = {"ARN_EVAL_LIVE": "1"}
        if o.flag("here") == "live":
            env["ARN_EVAL_HERE_LIVE"] = "1"
        if "elevenlabs" in mock:
            env["ARN_EVAL_SCRIPT_ONLY"] = "1"
        if o.has("judge"):
            env["ARN_EVAL_REVIEW"] = "1"
        if o.flag("pipeline") == "full":
            env["ARN_EVAL_FULL_PIPELINE"] = "1"
        return bash(o, "scripts/eval.sh", env=env)
    if o.verb == "regression":
        return dotnet_test(o, infra, "RegressionCorpus", {"ARN_EVAL_LIVE": "1"})
    if o.verb == "fallback":
        env = {"ARN_REPLAY_FALLBACK": "1"}
        if o.flag("fixture"):
            env["ARN_REPLAY_JOB"] = o.flag("fixture")
        return dotnet_test(o, infra, "FallbackReplay", env)
    if o.verb == "matrix":
        return dotnet_test(o, infra, "ScenarioMatrix", {"ARN_SCENARIO_MATRIX": "1"})
    return unknown(f"run {o.verb}")


# ──────────────────────────── trigger ────────────────────────────
def _trigger_voice(o, market, base, tok):
    requested = (o.flag("voice") or "").strip()
    if requested:
        if requested.lower().startswith("vp-"):
            return requested
        st, result = http("GET", f"{base}/api/voices/profiles", tok)
        items = result if isinstance(result, list) else (result or {}).get("Items") or (result or {}).get("Profiles") or (result or {}).get("VoiceProfiles") or (result or {}).get("profiles") or []
        for profile in items:
            if (profile.get("Name") or profile.get("name") or "").casefold() == requested.casefold():
                return profile.get("Id") or profile.get("id") or profile.get("VoiceProfileId")
        eprint(f"no voice profile named '{requested}'; pass --voice <voice-profile-id-or-name>")
        return None
    st, sched = http("GET", f"{base}/api/schedules/{market}", tok)
    voice = sched.get("VoiceProfileId") or sched.get("voiceProfileId") if isinstance(sched, dict) else None
    if voice:
        return voice
    eprint(f"no voice: market '{market}' has no schedule voice profile; pass --voice <voice-profile-id>")
    return None
def cmd_trigger(o):
    if o.help:
        return group_help("trigger", [("(flags)", "--market --duration --voice <profile-id-or-name> --content traffic,weather,fuel [--via post|poller] [--script-only] [--wait] [--timeout N]")])
    market, dur, via = o.flag("market") or "sydney", o.flag("duration") or "30", o.flag("via") or "post"
    if o.env == "local" and o.dry_run:
        base = resolve_env("dev")["functionApp"]
        tok = az_token("https://management.azure.com")
        if not tok:
            eprint("no management token — run `az login`.")
            return INFRA
        voice = _trigger_voice(o, market, base, tok)
        if not voice:
            return USAGE
        content = set(x for x in (o.flag("content") or "traffic,weather,fuel").split(",") if x)
        body = {"Location": market, "VoiceProfileId": voice, "TargetDurationSeconds": int(dur) if dur.isdigit() else 30,
                "Content": {"Traffic": "traffic" in content, "Weather": "weather" in content, "Fuel": "fuel" in content}}
        print(f"[dry-run] POST {base}/api/broadcast {json.dumps(body)}")
        return OK
    if o.env == "local":
        args = ["scripts/test-bulletin.sh", "--market", market, "--duration", dur]
        if o.flag("voice"):
            args.extend(["--voice", o.flag("voice")])
        if via == "poller":
            args.append("--schedule")
        if o.has("script-only"):
            args.append("--script-only")
        return bash(o, *args)
    if via == "poller":
        return trigger_via_poller(o, market, dur)
    content = set(x for x in (o.flag("content") or "traffic,weather,fuel").split(",") if x)
    url = f'{resolve_env(o.env)["functionApp"]}/api/broadcast'
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — run `az login`.")
        return INFRA
    voice = _trigger_voice(o, market, url.rsplit("/api/broadcast", 1)[0], tok)
    if not voice:
        return USAGE
    body = {"Location": market, "VoiceProfileId": voice, "TargetDurationSeconds": int(dur) if dur.isdigit() else 30,
            "Content": {"Traffic": "traffic" in content, "Weather": "weather" in content, "Fuel": "fuel" in content}}
    if o.dry_run:
        print(f"[dry-run] POST {url} {json.dumps(body)}")
        return OK
    status, resp = http("POST", url, tok, body)
    ok = bool(status and 200 <= status < 300)
    if o.json:
        out_json({"status": status, "response": resp})
    else:
        print(f"POST {url} -> {status}")
        print(json.dumps(resp, indent=2) if isinstance(resp, (dict, list)) else resp)
    if ok and o.has("wait"):
        jid = resp.get("JobId") if isinstance(resp, dict) else None
        if jid:
            timeout = int(o.flag("timeout")) if (o.flag("timeout") or "").isdigit() else 180
            print(f"waiting for {jid[:8]} to settle (timeout {timeout}s)…")
            rc, st = _watch_job(o, o.env, jid, timeout)
            if st in ("WaitingApproval", "Approved", "Published", "FallbackUsed"):
                job_get(o, o.env, jid, with_captures=False)
            return rc
    return OK if ok else FAIL


def _jobs_for_market(base, tok, market):
    """GET /api/jobs, filtered to one market (case-insensitive). None on transport failure."""
    status, resp = http("GET", f"{base}/api/jobs", tok)
    if not isinstance(resp, dict):
        return None
    return [j for j in resp.get("Items", []) if (j.get("Market") or "").lower() == market.lower()]


# `SchedulePollerService` treats a schedule as due when NextRunUtc <= now + ScheduleLeadTimeMinutes.
POLLER_LEAD_MIN = 15
ARM_PATTERN_ID = "arn-cli-arm"


def _minutes_until(iso):
    """Minutes from now until an ISO-8601 UTC instant, or None if it will not parse."""
    if not isinstance(iso, str) or not iso:
        return None
    try:
        when = datetime.fromisoformat(iso.replace("Z", "+00:00"))
    except ValueError:
        return None
    if when.tzinfo is None:
        when = when.replace(tzinfo=timezone.utc)
    return (when - datetime.now(timezone.utc)).total_seconds() / 60.0


def _arm_payload(sched, target):
    """Build the PUT that makes `target` the station's next occurrence, plus a label for it.

    A schedule carrying a Calendar ignores CronExpression on every date an assignment covers
    (`ScheduleOccurrenceResolver.NextRunUtc` uses the resolved pattern instead), so arming the
    base cron is a SILENT no-op there — it cost 16 minutes on test/sydney, 2026-08-24. When a
    calendar is present we therefore arm through the calendar itself: `ScheduleCalendar.ResolveDay`
    selects the SHORTEST covering window and lets a LATER list entry take an exact tie, so a
    single-date assignment appended last deterministically wins over whatever else covers the day
    (including a suppression). Schedules with no calendar keep the plain cron path unchanged.
    """
    cron = f"{target.minute} {target.hour} * * *"
    calendar = sched.get("Calendar")
    if not isinstance(calendar, dict):
        return {"CronExpression": cron, "Enabled": True}, "base cron"

    day = target.date().isoformat()
    # Drop any arm rows a previous interrupted run left behind, so retries stay idempotent.
    patterns = [p for p in (calendar.get("Patterns") or [])
                if (p.get("Id") or "").strip().lower() != ARM_PATTERN_ID]
    assignments = [a for a in (calendar.get("Assignments") or [])
                   if (a.get("PatternId") or "").strip().lower() != ARM_PATTERN_ID]
    patterns.append({"Id": ARM_PATTERN_ID, "Name": "arn CLI arm (temporary)", "CronExpression": cron})
    assignments.append({"StartDate": day, "EndDate": day,
                        "PatternId": ARM_PATTERN_ID, "Suppress": False})
    return ({"Enabled": True, "Calendar": {"Patterns": patterns, "Assignments": assignments}},
            f"calendar assignment for {day}")


def trigger_via_poller(o, market, dur):
    """Arm the market's own schedule to fire on the next `SchedulePollerTrigger` tick — the ONLY
    path that resolves real per-station voice/delivery/pronunciation/content config (manual
    POST /api/broadcast bypasses all of it and now hard-fails when no voice is supplied. There is no
    'fire now' API: we move the schedule's cron into the near future (inside its own
    NotifyBeforePlayoutMinutes lead window, so the poller sees it as due), wait for the poller
    to create + settle a job, then restore the schedule's original cron/enabled — always, even
    on failure or timeout."""
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — run `az login`.")
        return INFRA
    base = resolve_env(o.env)["functionApp"]
    sched_url = f"{base}/api/schedules/{market}"
    st, sched = http("GET", sched_url, tok)
    if not (st and 200 <= st < 300 and isinstance(sched, dict)):
        eprint(f"could not read schedule for market '{market}': {st} {sched}")
        return FAIL
    orig = {"CronExpression": sched.get("CronExpression"), "Enabled": sched.get("Enabled")}
    if isinstance(sched.get("Calendar"), dict):
        orig["Calendar"] = sched["Calendar"]
    try:
        tz = ZoneInfo(sched.get("Timezone") or "UTC")
    except Exception:
        tz = timezone.utc
    lead_min = sched.get("NotifyBeforePlayoutMinutes") or 15
    # Must be > (ScheduleLeadTimeMinutes - CurationWindowMinutes) = 10 min, so CurationDeadlineUtc is in the future.
    # Setting ahead_min to lead_min - 1 (e.g. 14 min) ensures NextRunAt is within the 15-min poller threshold
    # while giving a positive 4-minute curation window and 11-minute fallback deadline.
    ahead_min = max(11, lead_min - 1)
    target = datetime.now(tz) + timedelta(minutes=ahead_min)
    arm, how = _arm_payload(sched, target)
    if o.dry_run:
        print(f"[dry-run] arm {market} via {how}: {json.dumps(arm)} "
              f"(was enabled={orig['Enabled']}), wait for poller tick, then revert")
        return OK
    baseline_ids = {j.get("JobId") for j in (_jobs_for_market(base, tok, market) or [])}
    jid, timeout = None, 0
    try:
        st, resp = http("PUT", sched_url, tok, arm)
        if not (st and 200 <= st < 300):
            eprint(f"failed to arm schedule '{market}': {st} {resp}")
            return FAIL
        # Trust the server's own recomputed NextRunAt, not the fact that the PUT returned 200.
        # An arm that landed outside the lead window did not take (a calendar-governed date
        # overriding CronExpression is the way this happens), and failing here turns a silent
        # full-timeout wait into an immediate, explanatory error.
        nxt = resp.get("NextRunAt") if isinstance(resp, dict) else None
        due_in = _minutes_until(nxt)
        if due_in is None or due_in > POLLER_LEAD_MIN + 1:
            eprint(f"arm did not take: armed {market} via {how} but the server recomputed "
                   f"NextRunAt={nxt} "
                   f"({'unparseable' if due_in is None else format(due_in, '.0f') + 'm out'}), "
                   f"outside the {POLLER_LEAD_MIN}m poller lead window — nothing will fire.")
            return FAIL
        print(f"armed {market} via {how} — fires in ~{due_in:.0f}m (NextRunAt={nxt}); "
              f"waiting for the poller tick…")
        timeout = int(o.flag("timeout")) if (o.flag("timeout") or "").isdigit() else ahead_min * 60 + 150
        deadline = time.time() + timeout
        while time.time() < deadline:
            jobs = _jobs_for_market(base, tok, market) or []
            new = sorted((j for j in jobs if j.get("JobId") not in baseline_ids),
                         key=lambda j: j.get("CreatedAt", ""), reverse=True)
            if new:
                jid = new[0]["JobId"]
                break
            time.sleep(5)
    finally:
        rst, rresp = http("PUT", sched_url, tok, orig)
        if rst and 200 <= rst < 300:
            print(f"reverted {market} schedule to its original cron/enabled state")
        else:
            eprint(f"WARNING: failed to revert schedule '{market}' — restore manually: "
                   f"PUT {sched_url} {json.dumps(orig)} (got {rst} {rresp})")
    if not jid:
        eprint(f"timed out after {timeout}s waiting for a new job in market '{market}'.")
        return INFRA
    if o.json:
        out_json({"JobId": jid, "Market": market})
    else:
        print(f"job {jid[:8]} created")
    if o.has("wait"):
        wtimeout = int(o.flag("wait-timeout")) if (o.flag("wait-timeout") or "").isdigit() else 180
        rc, stt = _watch_job(o, o.env, jid, wtimeout)
        if stt in ("WaitingApproval", "Approved", "Published", "FallbackUsed"):
            job_get(o, o.env, jid, with_captures=False)
        return rc
    return OK


# ──────────────────────────── job ────────────────────────────
def cmd_job(o):
    if o.help or not o.verb:
        return group_help("job", [("get", "<id> record + timeline + captures"), ("logs", "<id> timeline"),
                                  ("traces", "<id> agent I/O (App Insights) [--full] [--hours N]"),
                                  ("captures", "<id> LLM calls [--download DIR]"), ("save", "<id> full diagnostics → job-diagnostics/<id>/ [--follow-fallback-chain]"), ("audio", "<id> download WAV [--out F|--url]"),
                                  ("bulletins", "<id,id,…>"), ("list", "recent jobs"), ("watch", "<id> poll to settled"),
                                  ("approve", "<id>"), ("reject", "<id> --notes …"), ("publish", "<id>"),
                                  ("regenerate", "<id>"), ("delete", "<id>"), ("purge", "delete ALL [--yes on test/prod]")])
    env = "dev" if o.env == "local" else o.env
    if o.verb == "list":
        url = f'{resolve_env(env)["functionApp"]}/api/jobs'
        if o.dry_run:
            print(f"[dry-run] GET {url}")
            return OK
        tok = az_token("https://management.azure.com")
        if not tok:
            eprint("no management token — `az login`.")
            return INFRA
        status, resp = http("GET", url, tok)
        if isinstance(resp, dict) and "Items" in resp and not o.json:
            items = resp["Items"]
            print(f"{resp.get('Total', len(items))} jobs (showing {min(len(items), 20)}):")
            for j in items[:20]:
                print(f"  {(j.get('JobId') or '')[:8]}  {str(j.get('Market', '')):<28} {j.get('Status', '')}")
            return OK if status and status < 300 else FAIL
        out_json(resp) if o.json else print(resp)
        return OK if status and status < 300 else FAIL
    if o.verb == "bulletins":
        ids = [x for x in (o.pos(0) or "").split(",") if x]
        if not ids:
            eprint("usage: arn job bulletins <id,id,…>")
            return USAGE
        rc = OK
        for b in ids:
            r = job_get(o, env, b, with_captures=False)
            if r != OK:
                rc = r
        return rc
    if o.verb == "purge":
        g = guard_env(o, "purge all jobs")
        if g is not None:
            return g
        return job_purge(o, env)
    idv = _job_id(o.pos(0))
    if not idv:
        return need("job", o.verb)
    if o.verb == "get":
        return job_get(o, env, idv, with_captures=True)
    if o.verb == "logs":
        return job_logs(o, env, idv)
    if o.verb == "captures":
        return job_captures(o, env, idv)
    if o.verb == "traces":
        return job_traces(o, env, idv)
    if o.verb == "delete":
        return job_delete(o, env, idv)
    if o.verb in ("approve", "reject", "publish", "regenerate"):
        return job_action(o, env, idv)
    if o.verb == "audio":
        return job_audio(o, env, idv)
    if o.verb == "watch":
        timeout = int(o.flag("timeout")) if (o.flag("timeout") or "").isdigit() else 180
        return _watch_job(o, env, idv, timeout)[0]
    if o.verb == "save":
        return job_save(o, env, idv)
    return unknown(f"job {o.verb}")


def job_action(o, env, jid):
    """POST a lifecycle action (approve/reject/publish/regenerate) to /api/jobs/{id}/<verb>."""
    verb = o.verb
    g = guard_env(o, f"{verb} job")
    if g is not None:
        return g
    body = None
    if verb == "reject":
        notes = o.flag("notes")
        if not notes:
            eprint('reject needs --notes "feedback for regeneration".')
            return USAGE
        body = {"Notes": notes, "Reason": o.flag("reason") or "Rejected by operator"}
    url = f'{resolve_env(env)["functionApp"]}/api/jobs/{jid}/{verb}'
    if o.dry_run:
        print(f"[dry-run] POST {url}" + (f" {json.dumps(body)}" if body else ""))
        return OK
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — `az login`.")
        return INFRA
    status, resp = http("POST", url, tok, body)
    ok = bool(status and 200 <= status < 300)
    if o.json:
        out_json({"status": status, "response": resp})
    else:
        msg = resp.get("Message") if isinstance(resp, dict) else resp
        print(f"{verb} {jid[:8]}: {status} — {msg}")
    return OK if ok else FAIL


def job_audio(o, env, jid):
    """Fetch the composed job via API and download its WAV.

    Audio.Data.Url is EITHER an absolute SAS URL (Broadcast:UseAudioProxy=false) or a relative
    backend path like `jobs/<id>/audio` (UseAudioProxy=true, which test and prod both run so the
    console can stream audio when the storage data plane is private). Handle both: a relative path
    is resolved against the env's Function App and fetched with the bearer token, since the proxy
    route sits behind Easy Auth.
    """
    if o.dry_run:
        print(f"[dry-run] GET /api/jobs/{jid} → download Audio.Data.Url")
        return OK
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — `az login`.")
        return INFRA
    base = resolve_env(env)["functionApp"]
    status, resp = http("GET", f'{base}/api/jobs/{jid}', tok)
    if not isinstance(resp, dict):
        eprint(f"could not fetch job {jid}: {status} {resp}")
        return FAIL
    url = ((resp.get("Audio") or {}).get("Data") or {}).get("Url")
    if not url:
        eprint("no audio URL on this job (not synthesised yet, or fallback).")
        return FAIL
    proxied = not str(url).lower().startswith(("http://", "https://"))
    if proxied:
        url = f'{base}/api/{str(url).lstrip("/")}'
    if o.has("url"):
        print(url)
        return OK
    dest = o.flag("out") or f"/tmp/{jid}.wav"
    try:
        req = urllib.request.Request(url)
        if proxied:
            req.add_header("Authorization", f"Bearer {tok}")
        with urllib.request.urlopen(req) as r, open(dest, "wb") as f:
            shutil.copyfileobj(r, f)
    except Exception as e:
        eprint(f"download failed: {e}")
        return FAIL
    print(f"saved {dest}")
    return OK


def cmd_coverage(o):
    if o.help or not o.verb:
        return group_help("coverage", [("show", "[--market M] [--since ISO] [--from ISO --to ISO] parser trend"),
                                       ("unknown", "[--market M] [--since ISO] unknown vocabulary to-do list")])

    params = {}
    for key in ("market", "since", "from", "to"):
        if o.flag(key):
            params[key] = o.flag(key)
    query = ("?" + urllib.parse.urlencode(params)) if params else ""
    env = o.env
    base = resolve_env(env)["functionApp"]
    path = "/api/coverage/unknown" if o.verb == "unknown" else "/api/coverage"
    if o.verb not in ("show", "unknown"):
        return unknown(f"coverage {o.verb}")
    url = f"{base}{path}{query}"
    if o.dry_run:
        print(f"[dry-run] GET {url}")
        return OK

    token = None if env == "local" else az_token("https://management.azure.com")
    if env != "local" and not token:
        eprint("no management token — run `az login`.")
        return INFRA
    status, response = http("GET", url, token)
    if not (status and 200 <= status < 300):
        eprint(f"coverage read failed ({status}): {response}")
        return FAIL
    if o.json:
        out_json(response)
        return OK

    items = response.get("Items", []) if isinstance(response, dict) else []
    if o.verb == "unknown":
        print(f"{response.get('Total', len(items)) if isinstance(response, dict) else len(items)} unknown vocabulary values:")
        for item in items:
            print(f"  [{item.get('Count', 0):>3}] {item.get('SourceId', '?')}: {item.get('Value', '')}"
                  f"  ({item.get('FirstSeenAt', '')[:19]} → {item.get('LastSeenAt', '')[:19]})")
            print(f"       TODO: {item.get('ToDo', '')}")
    else:
        print(f"{response.get('Total', len(items)) if isinstance(response, dict) else len(items)} coverage rows:")
        for item in items:
            print(f"  {item.get('SourceId', '?'):<16} {item.get('Field', ''):<18}"
                  f" structured={item.get('Structured', 0):>4}"
                  f" prose={item.get('Prose', 0):>4}"
                  f" prose-share={float(item.get('ProseShare', 0)):.1%}"
                  f" trend={'falling' if item.get('ProseFalling') else 'steady/rising'}")
    return OK


def cmd_health(o):
    if o.help:
        print("arn health — GET /api/health. usage: arn health --env dev|test|prod [--json]")
        return OK
    env = "dev" if o.env == "local" else o.env
    url = f'{resolve_env(env)["functionApp"]}/api/health'
    if o.dry_run:
        print(f"[dry-run] GET {url}")
        return OK
    status, resp = http("GET", url, az_token("https://management.azure.com"))
    if o.json:
        out_json({"status": status, "response": resp})
    else:
        print(f"health {env}: {status} — {json.dumps(resp) if isinstance(resp, dict) else resp}")
    return OK if status and 200 <= status < 300 else FAIL


_JOB_SETTLED = {"WaitingApproval", "Approved", "Published", "FallbackUsed", "Failed", "Rejected"}


def _watch_job(o, env, jid, timeout):
    """Poll the job's Status until it settles (or timeout). Returns (rc, last_status)."""
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        st = (_fetch_job(env, jid) or {}).get("Status")
        if st != last:
            print(f"  {jid[:8]}: {st}", flush=True)
            last = st
        if st in _JOB_SETTLED:
            return (FAIL if st in ("Failed", "Rejected") else OK), st
        time.sleep(5)
    eprint(f"timed out after {timeout}s (last status: {last}).")
    return INFRA, last


# ──────────────────────────── config (cross-env transfer) ────────────────────────────
def cmd_config(o):
    if o.help or not o.verb:
        return group_help("config", [("export", "[--sections a,b] [--out F] dump config bundle"),
                                     ("import", "<file> [--apply] (dry-run unless --apply)"),
                                     ("schema", "config JSON schema"),
                                     ("pending", "list pending imports (2nd-approver gate)"),
                                     ("approve", "<id> apply a pending import"),
                                     ("reject", "<id> discard a pending import")])
    if o.env == "local":
        eprint("config transfer targets a deployed env (dev/test/prod).")
        return USAGE
    base = f'{resolve_env(o.env)["functionApp"]}/api/config'
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — `az login`.")
        return INFRA
    if o.verb == "export":
        url = base + "/export" + (f"?sections={o.flag('sections')}" if o.flag("sections") else "")
        if o.dry_run:
            print(f"[dry-run] GET {url}")
            return OK
        status, resp = http("GET", url, tok)
        if not (status and status < 300):
            eprint(f"export failed ({status}): {resp}")
            return FAIL
        text = json.dumps(resp, indent=2) if isinstance(resp, (dict, list)) else str(resp)
        if o.flag("out"):
            Path(o.flag("out")).write_text(text)
            print(f"exported → {o.flag('out')}")
        else:
            print(text)
        return OK
    if o.verb == "schema":
        status, resp = http("GET", base + "/schema", tok)
        print(json.dumps(resp, indent=2) if isinstance(resp, (dict, list)) else resp)
        return OK if status and status < 300 else FAIL
    if o.verb == "import":
        f = o.pos(0)
        if not f:
            return need("config", "import <file>")
        try:
            bundle = json.loads(Path(f).read_text())
        except Exception as e:
            eprint(f"cannot read bundle {f}: {e}")
            return USAGE
        apply = o.has("apply")
        if apply:
            g = guard_env(o, "apply config import")
            if g is not None:
                return g
        url = base + "/import" + ("?dryRun=false" if apply else "")
        if o.dry_run:
            print(f"[dry-run] POST {url} (bundle from {f}; {'APPLY' if apply else 'dry-run'})")
            return OK
        status, resp = http("POST", url, tok, bundle)
        out_json({"status": status, "response": resp}) if o.json else print(
            json.dumps(resp, indent=2) if isinstance(resp, (dict, list)) else resp)
        return OK if status and 200 <= status < 300 else FAIL
    if o.verb == "pending":
        status, resp = http("GET", base + "/imports/pending", tok)
        out_json(resp) if o.json else print(
            json.dumps(resp, indent=2) if isinstance(resp, (dict, list)) else resp)
        return OK if status and status < 300 else FAIL
    if o.verb in ("approve", "reject"):
        pid = o.pos(0)
        if not pid:
            return need("config", o.verb)
        g = guard_env(o, f"{o.verb} config import")
        if g is not None:
            return g
        url = f"{base}/imports/pending/{pid}/{o.verb}"
        if o.dry_run:
            print(f"[dry-run] POST {url}")
            return OK
        status, resp = http("POST", url, tok)
        print(f"{o.verb} {pid}: {status} — {resp if not isinstance(resp, dict) else json.dumps(resp)}")
        return OK if status and 200 <= status < 300 else FAIL
    return unknown(f"config {o.verb}")


def _job_id(raw):
    """Accept a bare job id or an operator-console /jobs/<id> URL; return the id."""
    if not raw:
        return raw
    raw = raw.strip()
    if "/" in raw:
        raw = raw.rstrip("/").split("/")[-1].split("?")[0].split("#")[0]
    return raw


JOB_SAVE_COLUMNS = ["RowKey", "Status", "Market", "Station", "TargetDurationSeconds", "ScriptText", "LogsJson",
                    "IncidentsJson", "AllIncidentsJson", "CorridorsJson", "FlowRoadsJson", "GeneratorInputJson",
                    "FallbackSourceJobId", "FallbackStreak", "FallbackReason", "FallbackAppliedAt",
                    "AudioUrl", "ScheduledForUtc", "CreatedAt", "LastUpdatedAt"]


def _job_entity(env, jid, cols):
    # RowKey alone also matches the job's "JobIndex" partition stub (same RowKey,
    # no script/snapshot columns) since the 2026-07-16 Job_{market} partition change.
    account = storage_account(env)
    raw = az_run(["storage", "entity", "query", "--account-name", account, "--auth-mode", "login",
                  "--table-name", JOB_TABLE,
                  "--filter", f"RowKey eq '{jid}' and PartitionKey ne 'JobIndex'",
                  "--select", *cols, "-o", "json"])
    if raw is None:
        raise _storage_az_error(env)
    try:
        data = json.loads(raw)
    except Exception:
        raise _storage_az_error(env, "azure CLI returned invalid JSON")
    if not isinstance(data, dict):
        raise _storage_az_error(env, "azure CLI returned an unexpected entity-query response")
    items = data.get("items", [])
    if not isinstance(items, list):
        raise _storage_az_error(env, "azure CLI returned an unexpected entity-query response")
    return items[0] if items else None


def _logs_and_timeline(job):
    try:
        logs = json.loads(job.get("LogsJson") or "[]")
    except Exception:
        logs = []
    lines = []
    for e in logs:
        at = e.get("At", "")
        t = at.split("T")[1].split("Z")[0] if "T" in at else at
        lines.append(f"{t} | {e.get('Message', '')}")
    return logs, "\n".join(lines)


def _walk_fallback_chain(env, start):
    lines, seen, cur = [], set(), start
    while cur and cur not in seen:
        seen.add(cur)
        j = _job_entity(env, cur, ["RowKey", "Status", "FallbackReason", "FallbackSourceJobId",
                                   "FallbackStreak", "ScheduledForUtc"])
        if not j:
            lines.append(f"  (chain broken — {cur} not found)")
            break
        lines.append(f"  {str(j.get('ScheduledForUtc')):30}  {j.get('RowKey')}  {str(j.get('Status')):15}  "
                     f"reason={j.get('FallbackReason')}  streak={j.get('FallbackStreak')}")
        cur = j.get("FallbackSourceJobId")
    return "\n".join(lines)

def _download_captures_flat(env, jid, dest):
    dest.mkdir(parents=True, exist_ok=True)
    if az_run(["storage", "blob", "download-batch", "--account-name", storage_account(env), "--auth-mode", "login",
               "--source", CAPTURE_CONTAINER, "--pattern", f"{jid}/*", "--destination", str(dest), "-o", "none"]) is None:
        raise _storage_az_error(env)
    nested = dest / jid
    if nested.is_dir():
        for f in nested.iterdir():
            f.rename(dest / f.name)
        nested.rmdir()


def _save_summary(job, logs, captures, fallback_chain):
    jid = job.get("RowKey")
    md = [f"# Job {jid} — diagnostic brief\n"]
    md.append(f"- **Market / Station:** {job.get('Market')} / {job.get('Station')}")
    md.append(f"- **Status:** {job.get('Status')}")
    md.append(f"- **Target duration:** {job.get('TargetDurationSeconds')}s")
    md.append(f"- **Audio URL:** {job.get('AudioUrl')}")
    if job.get("FallbackStreak"):
        md.append(f"- **⚠ FallbackUsed** — reason: `{job.get('FallbackReason')}`, "
                  f"streak: {job.get('FallbackStreak')}, source job: `{job.get('FallbackSourceJobId')}`")
        if job.get("FallbackSourceJobId"):
            md.append("  - **Gotcha:** `CorridorsJson`/`IncidentsJson`/`ScriptText` on this job are COPIED from the "
                      "fallback source job, not this job's own fetch. This job's own honest traffic-fetch numbers are in "
                      "its own `LogsJson` (the \"Traffic fetched: N corridor(s), M incident(s)\" line below).")
        else:
            md.append("  - No fallback source job (generic filler cart applied). `CorridorsJson`/`IncidentsJson` ARE "
                      "this job's own genuine fetch; only `ScriptText`/`AudioUrl` were replaced by the filler cart.")
    fetched = next((l["Message"] for l in logs if str(l.get("Message", "")).startswith("Traffic fetched:")), None)
    if fetched:
        md.append(f"- **This job's own traffic fetch:** {fetched}")
    md.append(f"\n## Timeline ({len(logs)} entries — full text in `logs_timeline.txt`)\n")
    key = ("Starting orchestration", "Generation attempt", "Short bulletin", "Final grounding check failed",
           "AI safety review", "Fallback applied", "Broadcast ready", "Script generated successfully")
    for e in logs:
        msg = str(e.get("Message", ""))
        if any(msg.startswith(k) for k in key):
            at = e.get("At", "")
            t = at.split("T")[1].split("Z")[0] if "T" in at else at
            md.append(f"- `{t}` {msg if len(msg) <= 160 else msg[:157] + '...'}")
    md.append("\n## LLM call captures\n")
    if not captures:
        md.append("None found (capture is opt-in `Debug:CaptureLlmCalls` + rolling-windowed `Debug:MaxCapturedJobs`; "
                  "absence is normal — use the timeline above).")
    else:
        by = {}
        for c in captures:
            parts = c["name"].rsplit("/", 1)[-1].split("-")
            k = parts[1] if len(parts) >= 3 else "unknown"
            by[k] = by.get(k, 0) + 1
        md.append(f"**{len(captures)} calls captured** — " + ", ".join(f"{v} {k}" for k, v in sorted(by.items())))
        md.append("\nEach `llm_calls/*.json` has `RenderedPrompt`, `ResponseBody` (incl. `usage`), "
                  "`SystemInstructions`, `Model`, `Stage`, `Attempt`, `ElapsedMs`.")
    if fallback_chain:
        md.append("\n## Fallback chain\n```")
        md.append(fallback_chain)
        md.append("```")
    md.append("\n## Files\n- `job.json` — full raw table entity\n- `logs_timeline.txt` — every LogsJson entry")
    if captures:
        md.append("- `llm_calls/*.json` — every captured prompt/response pair")
    if fallback_chain:
        md.append("- `fallback_chain.txt`")
    return "\n".join(md) + "\n"


def _expand_gz_columns(job):
    """Backend compresses oversized JSON columns as "gz:" + base64(gzip(json))
    (TableStorageJobRepository.FitTableProperty). Expand at export time so job.json
    and every downstream diagnostic keeps seeing plain JSON strings."""
    import base64
    import gzip
    for k, v in list(job.items()):
        if isinstance(v, str) and v.startswith("gz:"):
            try:
                job[k] = gzip.decompress(base64.b64decode(v[3:])).decode("utf-8")
            except (ValueError, OSError):
                pass  # leave the raw value; better than dropping it
    return job


def job_save(o, env, jid):
    """Native diagnose-job: write job.json + logs_timeline.txt + llm_calls/*.json + SUMMARY.md to job-diagnostics/<id>/."""
    out = Path(o.flag("out")) if o.flag("out") else ROOT / "job-diagnostics" / jid
    if o.dry_run:
        print(f"[dry-run] save job {jid} → {out}/ (job.json, logs_timeline.txt, llm_calls/*.json, SUMMARY.md)")
        return OK
    job = _expand_gz_columns(_job_entity(env, jid, JOB_SAVE_COLUMNS) or {}) or None
    if not job:
        eprint(f"job {jid} not found on {storage_account(env)} (wrong --env?).")
        return FAIL
    out.mkdir(parents=True, exist_ok=True)
    (out / "job.json").write_text(json.dumps(job, indent=2, default=str))
    logs, timeline = _logs_and_timeline(job)
    (out / "logs_timeline.txt").write_text(timeline)
    fallback_chain = None
    if o.has("follow-fallback-chain") and job.get("FallbackSourceJobId"):
        fallback_chain = _walk_fallback_chain(env, jid)
        (out / "fallback_chain.txt").write_text(fallback_chain)
    caps = [] if o.has("no-captures") else (_captures(env, jid) or [])
    if caps:
        _download_captures_flat(env, jid, out / "llm_calls")
    (out / "SUMMARY.md").write_text(_save_summary(job, logs, caps, fallback_chain))
    print(f"saved diagnostics → {out}/  ({len(caps)} capture(s))")
    return OK


def _fetch_job(env, jid):
    # Delegate to the guarded _job_entity so this path also excludes the JobIndex
    # stub row (PK=JobIndex, no snapshot columns) added by the Job_{market} partition
    # change; a bare RowKey filter otherwise returns the stub with all fields None.
    return _job_entity(env, jid, JOB_COLUMNS)


def _timeline(job):
    try:
        logs = json.loads(job.get("LogsJson") or "[]")
    except Exception:
        logs = []
    out = []
    for e in logs:
        at = e.get("At", "")
        t = at.split("T")[1].split("Z")[0] if "T" in at else at
        out.append(f"{t} | {e.get('Message', '')}")
    return "\n".join(out)


def _captures(env, jid):
    data = az_json(["storage", "blob", "list", "--account-name", storage_account(env), "--auth-mode", "login",
                    "--container-name", CAPTURE_CONTAINER, "--prefix", f"{jid}/",
                    "--query", "[].{name:name, size:properties.contentLength}", "-o", "json"])
    if data is None:
        raise _storage_az_error(env)
    if not isinstance(data, list):
        raise _storage_az_error(env, "azure CLI returned an unexpected blob-list response")
    return data


def _parse_msgs(raw):
    if not raw:
        return []
    try:
        msgs = json.loads(raw)
    except Exception:
        return [("raw", str(raw))]
    out = []
    for m in msgs:
        role, parts = m.get("role", "?"), m.get("parts", [])
        text = "".join(p.get("content", "") if isinstance(p, dict) else str(p) for p in parts) \
            if isinstance(parts, list) else str(parts)
        out.append((role, text))
    return out


def job_get(o, env, jid, with_captures):
    if o.dry_run:
        print(f"[dry-run] az storage entity query {JOB_TABLE} RowKey='{jid}' on {storage_account(env)}")
        return OK
    job = _fetch_job(env, jid)
    if not job:
        eprint(f"no job {jid} in {storage_account(env)} (wrong --env?).")
        return USAGE
    tl = _timeline(job)
    caps = _captures(env, jid) if with_captures else []
    if o.json:
        out_json({"job": {k: v for k, v in job.items() if k != "LogsJson"}, "timeline": tl, "captures": caps})
        return OK
    print(f"job {job.get('RowKey')}  [{job.get('Status')}]  {job.get('Market')} / {job.get('Station')}  "
          f"target={job.get('TargetDurationSeconds')}s")
    if job.get("FallbackReason"):
        print(f"  fallback: {job.get('FallbackReason')} (streak {job.get('FallbackStreak')})")
    if job.get("AudioUrl"):
        print(f"  audio: {str(job.get('AudioUrl'))[:80]}…")
    if job.get("ScriptText"):
        print(f"\nSCRIPT:\n{job.get('ScriptText')}")
    print(f"\nTIMELINE:\n{tl}")
    if with_captures:
        print(f"\nCAPTURES: {len(caps)} file(s)" + (":" if caps else " (none — Debug:CaptureLlmCalls off or aged out)"))
        for c in caps:
            print(f"  {c.get('name')}  ({c.get('size')}B)")
    return OK


def job_logs(o, env, jid):
    if o.dry_run:
        print(f"[dry-run] fetch {JOB_TABLE} {jid} logs on {storage_account(env)}")
        return OK
    job = _fetch_job(env, jid)
    if not job:
        eprint(f"no job {jid} in {storage_account(env)}.")
        return USAGE
    tl = _timeline(job)
    out_json({"timeline": tl}) if o.json else print(tl)
    return OK


def job_captures(o, env, jid):
    if o.dry_run:
        print(f"[dry-run] list {jid}/ in {CAPTURE_CONTAINER}@{storage_account(env)}")
        return OK
    caps = _captures(env, jid)
    dest = o.flag("download")
    if dest:
        if az_run(["storage", "blob", "download-batch", "--account-name", storage_account(env),
                   "--auth-mode", "login", "--source", CAPTURE_CONTAINER, "--pattern", f"{jid}/*",
                   "--destination", dest]) is None:
            raise _storage_az_error(env)
    if o.json:
        out_json(caps)
    else:
        print(f"{len(caps)} capture(s) for {jid}:")
        for c in caps:
            print(f"  {c.get('name')}  ({c.get('size')}B)")
    return OK


def job_traces(o, env, jid):
    hours = o.flag("hours") if (o.flag("hours") or "").isdigit() else "3"
    if o.dry_run:
        print(f"[dry-run] App Insights KQL over {ain_component(env)} for invoke_agent spans of job {jid} (ago({hours}h))")
        return OK
    appid = az_run(["monitor", "app-insights", "component", "show", "--app", ain_component(env),
                    "-g", ain_rg(env), "--subscription", SUB, "--query", "appId", "-o", "tsv"])
    if not appid:
        eprint("could not resolve App Insights appId — `az login` / access?")
        return INFRA
    appid, span = appid.strip(), f"ago({hours}h)"

    def kql(q):
        data = az_json(["monitor", "app-insights", "query", "--app", appid, "--subscription", SUB,
                        "--analytics-query", q, "-o", "json"])
        if data is None:
            reason = az_last_error() or "azure CLI returned no data"
            raise AzError(f"App Insights query failed for {ain_component(env)}: {reason}")
        if not isinstance(data, dict):
            raise AzError(f"App Insights query returned an unexpected response for {ain_component(env)}")
        tbls = data.get("tables", [])
        return tbls[0]["rows"] if tbls else []

    ops = kql(f"let ops = traces | where timestamp > {span} | where message contains '{jid}' or "
              f"customDimensions contains '{jid}' | distinct operation_Id; dependencies | where timestamp > {span} "
              f"| where name startswith 'invoke_agent' | where operation_Id in (ops) | distinct operation_Id")
    if not ops:
        eprint(f"no pipeline operation for job {jid} in the last {hours}h.")
        return FAIL
    op = ops[0][0]
    rows = kql(f"dependencies | where operation_Id == '{op}' | where name startswith 'invoke_agent' "
               f"| extend inp = tostring(customDimensions['gen_ai.input.messages']), "
               f"out = tostring(customDimensions['gen_ai.output.messages']), "
               f"cf = tostring(customDimensions['microsoft.foundry.content_filter.results']) "
               f"| project timestamp, name, duration, inp, out, cf | order by timestamp asc")
    results = []
    for ts, name, dur, inp, outp, cf in rows:
        msgs = _parse_msgs(inp)
        focus = [t for r, t in msgs if r not in ("developer", "system")]
        tail = focus[-1] if focus else (msgs[-1][1] if msgs else "")
        results.append({"agent": name.replace("invoke_agent ", ""), "seconds": round(float(dur) / 1000, 2),
                        "blocked": '"blocked": true' in (cf or ""),
                        "input_full": [{"role": r, "text": t} for r, t in msgs], "input_tail": tail,
                        "output": "".join(t for _, t in _parse_msgs(outp))})
    if o.json:
        out_json({"operation_Id": op, "agents": results})
        return OK
    print(f"# agent trace — job {jid}\noperation_Id: {op}  ·  {len(results)} invocations\n")
    for i, r in enumerate(results, 1):
        print("=" * 74)
        print(f"[{i}] {r['agent']}  {r['seconds']}s" + ("  ⚠ content-filter BLOCKED" if r["blocked"] else ""))
        print("=" * 74)
        if o.has("full"):
            for m in r["input_full"]:
                print(f"  IN[{m['role']}]: {m['text']}\n")
        else:
            print(f"  INPUT (task/feedback): {r['input_tail'][:500]}{'…' if len(r['input_tail']) > 500 else ''}")
        print(f"  OUTPUT: {r['output']}\n")
    return OK


# ──────────────────────────── perf ────────────────────────────
def cmd_perf(o):
    if o.help or not o.verb:
        return group_help("perf", [("load", "--scenario health|api-read|pipeline-burst|soak [--jobs N]"),
                                   ("profile", "per-phase latency (real services)")])
    if o.verb == "load":
        environment = "dev" if o.env == "local" else o.env
        scenario = o.flag("scenario") or "health"
        extra = ["--jobs", o.flag("jobs")] if o.flag("jobs") else []
        return delegate(o, ["bash", "test/load/run.sh", scenario, environment, *extra])
    if o.verb == "profile":
        return dotnet_test(o, "test/ArnBulletinGenerator.Infrastructure.Tests", "RealServiceProfiling", None)
    return unknown(f"perf {o.verb}")


# ──────────────────────────── safety (native → deployed arn-safety) ────────────────────────────
def cmd_safety(o):
    if o.help or not o.verb:
        return group_help("safety", [("check", "<script text> — one script through the gate")])
    if o.env == "local":
        eprint("safety needs a deployed env: --env dev")
        return INFRA
    url = f'{resolve_env(o.env)["foundryProject"]}/agents/arn-safety/endpoint/protocols/openai/responses?api-version=v1'
    tmpl = (ROOT / "prompts" / "safety.md").read_text()
    if o.dry_run:
        print(f"[dry-run] POST {url}  (rendered prompts/safety.md as input; token scope https://ai.azure.com)")
        return OK
    tok = az_token("https://ai.azure.com")
    if not tok:
        eprint("no ai.azure.com token — run `az login`.")
        return INFRA

    def review(script):
        _, resp = http("POST", url, tok, {"input": tmpl.replace("{{script}}", script)})
        if not isinstance(resp, dict):
            return None
        for item in resp.get("output", []) or []:
            for c in item.get("content", []) or []:
                if c.get("type") in ("output_text", "text") and c.get("text"):
                    try:
                        return json.loads(c["text"])
                    except Exception:
                        return None
        return None

    if o.verb == "check":
        script = " ".join(o.passthrough) if o.passthrough else o.pos(0)
        if not script:
            eprint("usage: arn safety check <script text>")
            return USAGE
        v = review(script)
        out_json(v) if o.json else print(json.dumps(v) if v else "(no JSON verdict — content-filter refusal or error)")
        return FAIL if (v and v.get("safe") is False) else OK
    if o.verb == "run":
        eprint("the adversarial safety corpus is not part of this repository; use `arn safety check <script>`.")
        return USAGE
    return unknown(f"safety {o.verb}")


# ──────────────────────────── report ────────────────────────────
def cmd_report(o):
    if o.help or not o.verb:
        return group_help("report", [("scorecard", "[name] — list or print eval scorecards"), ("compare", "<a> <b>")])
    d = ROOT / "output" / "eval"
    if not d.exists():
        eprint(f"no scorecards yet ({d} missing) — run `arn run eval`.")
        return USAGE
    files = sorted(d.glob("*.md"), key=lambda f: f.stat().st_mtime, reverse=True)
    if o.verb == "scorecard":
        pick = o.pos(0)
        if not pick:
            print(f"{len(files)} scorecards in output/eval:")
            for f in files[:40]:
                print(f"  {f.name}")
            return OK
        m = next((f for f in files if pick.lower() in f.name.lower()), None)
        if not m:
            eprint(f"no scorecard matching '{pick}'.")
            return USAGE
        print(m.read_text())
        return OK
    if o.verb == "compare":
        a, b = o.pos(0), o.pos(1)
        if not a or not b:
            eprint("usage: arn report compare <a> <b>")
            return USAGE
        for name in (a, b):
            f = next((x for x in files if name.lower() in x.name.lower()), None)
            print(f"===== {name} =====")
            print(f.read_text() if f else "(not found)")
        return OK
    return unknown(f"report {o.verb}")


# ──────────────────────────── ops: resources & auth ────────────────────────────
# All three environments live in ONE subscription ("IBP Sub"), separated by resource group
# (IBP-AUE-{DEV,TST,PRD}-RSG-APP01) — verified live 2026-08-13. ARN_SUB still overrides.
SUBS = {"dev": SUB, "test": SUB, "prod": SUB}
ADO_ORG = "https://dev.azure.com/australian-radio-network"
ADO_PROJECT = "cb94a972-86c7-40b3-a9c3-3bb106955f9a"  # project id (rename-proof; "Traffic POC" → "Information Bulletin")
ADO_RESOURCE = "499b84ac-1321-427f-aa17-267ca6975798"
ADO_TENANT = "91d5bc47-4df3-4ad0-a5ae-3d3249eceaf1"
ADO_PIPELINE = 108
OWNER_ROLE_ID = "8e3af657-a8ff-443c-a75c-2fe8c4bcb635"
FOUNDRY_RESOURCE = "https://ai.azure.com"
FOUNDRY_API = "2025-05-15-preview"


def sub_for(env):
    return SUBS.get(env) or os.environ.get("ARN_SUB")


def sub_args(env):
    s = sub_for(env)
    return ["--subscription", s] if s else []


def fna_app(env):
    return f"IBP-AUE-{env_slug(env).upper()}-FNA-APP01"


def swa_app(env):
    return f"IBP-AUE-{env_slug(env).upper()}-SWA-APP01"


def law_rg(env):
    return f"IBP-AUE-{env_slug(env).upper()}-RSG-LAW01"


def law_ws(env):
    return f"IBP-AUE-{env_slug(env).upper()}-LAW-APP01"


def guard_env(o, action):
    """test/prod mutating ops require an explicit --yes."""
    if o.env in ("test", "prod") and not o.has("yes"):
        eprint(f"refusing to {action} on '{o.env}' without --yes (guardrail).")
        return USAGE
    return None


def az_full(args):
    try:
        r = subprocess.run(["az", *args], capture_output=True, text=True)
        return r.returncode, r.stdout, r.stderr
    except FileNotFoundError:
        return 127, "", "az CLI not found"


# ──────────────────────────── pim ────────────────────────────
def _signed_in_obj_id():
    return (az_run(["ad", "signed-in-user", "show", "--query", "id", "-o", "tsv"]) or "").strip() or None


def _owner_active(env, oid=None):
    """True if the signed-in user holds Owner anywhere; None if undeterminable."""
    oid = oid or _signed_in_obj_id()
    if not oid:
        return None
    q = az_json(["role", "assignment", "list", "--assignee", oid, "--all",
                 "--query", "[?roleDefinitionName=='Owner'].scope", "-o", "json"])
    return None if q is None else len(q) > 0


def _arm_token_iat():
    """iat (epoch) of the cached ARM access token; None if unavailable."""
    tok = az_token("https://management.azure.com")
    if not tok:
        return None
    try:
        payload = tok.split(".")[1]
        payload += "=" * (-len(payload) % 4)
        return json.loads(base64.urlsafe_b64decode(payload)).get("iat")
    except (IndexError, ValueError):
        return None


def _owner_activated_at(env, oid):
    """createdOn (epoch) of the sub-scope Owner assignment for oid, or None."""
    s = sub_for(env)
    if not s or not oid:
        return None
    rows = az_json(["rest", "--method", "get", "--url",
                    f"https://management.azure.com/subscriptions/{s}/providers/Microsoft.Authorization/"
                    f"roleAssignments?api-version=2022-04-01&$filter=principalId eq '{oid}'"]) or {}
    for r in rows.get("value", []):
        p = r.get("properties", {})
        if p.get("roleDefinitionId", "").endswith(OWNER_ROLE_ID) and p.get("scope") == f"/subscriptions/{s}":
            try:
                return datetime.strptime(p["createdOn"][:19], "%Y-%m-%dT%H:%M:%S").replace(
                    tzinfo=timezone.utc).timestamp()
            except (KeyError, ValueError):
                return None
    return None


def _remint_arm_token():
    """Evict cached access tokens (refresh token kept) so the next az call mints fresh ones.

    ARM caches a permission snapshot per TOKEN: a token minted before a PIM activation keeps
    returning AuthorizationFailed for up to ~30 min after the assignment is live, and
    `az account list --refresh` does NOT remint it (bit us 2026-08-07)."""
    cache = Path.home() / ".azure" / "msal_token_cache.json"
    try:
        d = json.loads(cache.read_text())
    except (OSError, ValueError):
        return False
    d["AccessToken"] = {}
    try:
        cache.write_text(json.dumps(d))
    except OSError:
        return False
    return True


def _ensure_fresh_arm_token(env, oid):
    """Remint the ARM token if it may predate the Owner activation (see _remint_arm_token).
    iat is backdated ~5 min for clock skew, so a just-minted token can look stale — reminting
    in that band is a harmless extra silent refresh."""
    act = _owner_activated_at(env, oid)
    iat = _arm_token_iat()
    if not act or not iat or iat >= act:
        return
    eprint("ARM token predates the Owner activation — evicting the msal token cache and reminting…")
    if _remint_arm_token():
        az_token("https://management.azure.com")  # mint the replacement now
    else:
        eprint("⚠ could not edit ~/.azure/msal_token_cache.json — if ops 403: `az account clear && az login`.")


def _pim_activate(env, oid, hours="8", why="arn ops session"):
    """SelfActivate Owner; RoleAssignmentExists (already active) counts as success."""
    s = sub_for(env)
    rid = str(uuid.uuid4())
    url = (f"https://management.azure.com/subscriptions/{s}/providers/Microsoft.Authorization/"
           f"roleAssignmentScheduleRequests/{rid}?api-version=2020-10-01")
    body = {"properties": {"principalId": oid,
                           "roleDefinitionId": f"/subscriptions/{s}/providers/Microsoft.Authorization/roleDefinitions/{OWNER_ROLE_ID}",
                           "requestType": "SelfActivate",
                           "scheduleInfo": {"startDateTime": None,
                                            "expiration": {"type": "AfterDuration", "duration": f"PT{hours}H"}},
                           "justification": why}}
    rc, sout, serr = az_full(["rest", "--method", "put", "--url", url, "--body", json.dumps(body),
                              "--headers", "Content-Type=application/json"])
    if rc == 0:
        print(f"Owner activation requested on {env} (PT{hours}H).")
        return OK
    if "RoleAssignmentExists" in (serr or "") + (sout or ""):
        print(f"Owner already active on {env}.")
        return OK
    eprint("PIM self-activation failed (often needs interactive MFA or an eligibility schedule). Detail:")
    eprint((serr or sout).strip()[:600])
    eprint(f"manual: portal → PIM → My roles → Owner → Activate on subscription {s}.")
    return FAIL


def _pim_wait_active(env, timeout=180):
    """Poll until the Owner role assignment exists. True on success."""
    deadline = time.time() + timeout
    n = 0
    while time.time() < deadline:
        if n:
            az_run(["account", "list", "--all", "--refresh", "-o", "none"])
        if _owner_active(env):
            return True
        n += 1
        print(f"  …Owner not active yet (poll {n}); waiting 15s", flush=True)
        time.sleep(15)
    return False


def _pim_preflight(o):
    """Self-heal privileged-op auth: activate Owner if needed and remint a stale ARM token.
    Warns and continues on failure — the actual op gives the definitive error."""
    if o.env == "local" or o.dry_run:
        return
    oid = _signed_in_obj_id()
    if not oid:
        eprint("⚠ not signed in (`az login`) — privileged ops will fail.")
        return
    if not _owner_active(o.env, oid):
        eprint(f"Owner not active on {o.env} — self-activating via PIM…")
        if _pim_activate(o.env, oid) != OK or not _pim_wait_active(o.env, 120):
            eprint("⚠ Owner still not active — privileged ops may 403 (portal → PIM → My roles).")
            return
    _ensure_fresh_arm_token(o.env, oid)


def cmd_pim(o):
    if o.help or not o.verb:
        return group_help("pim", [("status", "is Owner active on the subscription?"),
                                  ("activate", "self-activate Owner via PIM [--hours N --why TEXT]"),
                                  ("wait", "block until Owner is active [--timeout S]")])
    s = sub_for(o.env)
    if o.verb == "status":
        act = _owner_active(o.env)
        if act is None:
            eprint("not signed in / cannot read role assignments — `az login`.")
            return INFRA
        print(f"Owner on {o.env}: {'ACTIVE' if act else 'eligible (NOT active)'}")
        return OK if act else FAIL
    if o.verb == "activate":
        oid = _signed_in_obj_id()
        if not oid:
            eprint("not signed in — `az login`.")
            return INFRA
        if not s:
            eprint(f"no subscription id for '{o.env}' — set ARN_SUB or use --env dev.")
            return USAGE
        hours = o.flag("hours") if (o.flag("hours") or "").isdigit() else "8"
        why = o.flag("why") or "arn ops session"
        if o.dry_run:
            print(f"[dry-run] SelfActivate Owner on /subscriptions/{s} for PT{hours}H")
            return OK
        return _pim_activate(o.env, oid, hours, why)
    if o.verb == "wait":
        timeout = int(o.flag("timeout")) if (o.flag("timeout") or "").isdigit() else 180
        if not _pim_wait_active(o.env, timeout):
            eprint(f"timed out after {timeout}s waiting for Owner on {o.env}.")
            return INFRA
        _ensure_fresh_arm_token(o.env, _signed_in_obj_id())
        print(f"Owner ACTIVE on {o.env}.")
        return OK
    return unknown(f"pim {o.verb}")


# ──────────────────────────── settings ────────────────────────────
def _secretish(name):
    return any(s in name.upper() for s in ("KEY", "SECRET", "PASSWORD", "TOKEN", "CONNECTIONSTRING"))


def _redact(name, val):
    if val and _secretish(name) and not str(val).startswith("@Microsoft"):
        return (str(val)[:4] + "…") if len(str(val)) > 4 else "***"
    return val


def _appsettings_list(env, key=None):
    data = az_json(["functionapp", "config", "appsettings", "list", "-n", fna_app(env), "-g", ain_rg(env),
                    *sub_args(env), "-o", "json"])
    if data is None:
        return None
    return sorted((x["name"], x["value"]) for x in data if not key or key.lower() in x["name"].lower())


def _appsettings_set(o, key, val, restart=True):
    fna, rg = fna_app(o.env), ain_rg(o.env)
    if o.dry_run:
        print(f"[dry-run] az functionapp config appsettings set -n {fna} -g {rg} --settings {key}=***")
        if restart:
            print(f"[dry-run] az functionapp restart -n {fna} -g {rg}")
        return OK
    if az_json(["functionapp", "config", "appsettings", "set", "-n", fna, "-g", rg, *sub_args(o.env),
                "--settings", f"{key}={val}", "-o", "json"]) is None:
        eprint("set failed (creds/PIM?).")
        return FAIL
    print(f"set {key} on {fna}")
    if restart:
        az_run(["functionapp", "restart", "-n", fna, "-g", rg, *sub_args(o.env), "-o", "none"])
        print("restarted — allow ~1-2 min for workers to reload.")
    return OK


def cmd_settings(o):
    if o.help or not o.verb:
        return group_help("settings", [("get", "[key-substr] read Function App settings"),
                                       ("list", "all settings"), ("set", "<key> <value> [--no-restart]")])
    if o.env == "local":
        eprint("local settings live in src/.../local.settings.json (not managed here).")
        return USAGE
    if o.verb in ("get", "list"):
        rows = _appsettings_list(o.env, o.pos(0) if o.verb == "get" else None)
        if rows is None:
            eprint("could not read app settings (creds/PIM?).")
            return INFRA
        if o.json:
            out_json({n: _redact(n, v) for n, v in rows})
        else:
            for n, v in rows:
                print(f"{n} = {_redact(n, v)}")
        return OK
    if o.verb == "set":
        g = guard_env(o, "set app settings")
        if g is not None:
            return g
        k, v = o.pos(0), o.pos(1)
        if not k or v is None:
            return need("settings", "set <key> <value>")
        return _appsettings_set(o, k, v, restart=not o.has("no-restart"))
    return unknown(f"settings {o.verb}")


# ──────────────────────────── logs ────────────────────────────
def cmd_logs(o):
    if o.help:
        print("arn logs — Function App logs via Log Analytics (FunctionAppLogs).\n"
              "  arn logs [--job <id>] [--minutes N] [--grep TEXT] --env dev|test|prod [--json]")
        return OK
    if o.env == "local":
        eprint("local logs: see the `arn env up` / func console output.")
        return USAGE
    wsid = (az_run(["monitor", "log-analytics", "workspace", "show", "-g", law_rg(o.env), "-n", law_ws(o.env),
                    *sub_args(o.env), "--query", "customerId", "-o", "tsv"]) or "").strip()
    if not wsid:
        eprint(f"could not resolve LAW workspace {law_ws(o.env)} (creds/PIM?).")
        return INFRA
    mins = o.flag("minutes") if (o.flag("minutes") or "").isdigit() else "10"
    q = f"FunctionAppLogs | where TimeGenerated > ago({mins}m)"
    if o.flag("job"):
        q += f" | where Message has '{o.flag('job')}'"
    if o.flag("grep"):
        q += f" | where Message has '{o.flag('grep')}'"
    q += " | project TimeGenerated, Level, Message | order by TimeGenerated asc | take 500"
    if o.dry_run:
        print(f"[dry-run] az monitor log-analytics query -w {wsid} --analytics-query \"{q}\"")
        return OK
    rows = az_json(["monitor", "log-analytics", "query", "-w", wsid, "--analytics-query", q, "-o", "json"])
    if rows is None:
        eprint("log query failed.")
        return FAIL
    if o.json:
        out_json(rows)
        return OK
    for r in rows:
        print(f"{str(r.get('TimeGenerated', ''))[:19]}  {str(r.get('Level', ''))[:4]:<4} {r.get('Message', '')}")
    if not rows:
        print(f"(no FunctionAppLogs in last {mins}m — LAW ingestion lags 1-3 min)")
    return OK


# ──────────────────────────── deploy ────────────────────────────
def cmd_deploy(o):
    if o.help or not o.verb:
        return group_help("deploy", [("all", "one-shot: self-heal PIM/token + api + web [--skip-web|--skip-api]"),
                                     ("web", "build web → Static Web App"),
                                     ("api", "publish Functions code → zip-deploy + restart"),
                                     ("restart", "restart the Function App"),
                                     ("full", "deploy.sh infra+code+web [--skip-infra|--skip-code|--skip-web]"),
                                     ("park", "stop the app"), ("resume", "start the app"),
                                     ("smoke", "post-deploy smoke test")])
    if o.env == "local":
        eprint("deploy targets dev/test/prod (use `arn env up` for local).")
        return USAGE
    _pim_preflight(o)
    if o.verb == "all":
        return deploy_all(o)
    if o.verb == "web":
        return deploy_web(o)
    if o.verb == "api":
        return deploy_api(o)
    if o.verb == "restart":
        g = guard_env(o, "restart")
        if g is not None:
            return g
        rg, fna = ain_rg(o.env), fna_app(o.env)
        if o.dry_run:
            print(f"[dry-run] az functionapp restart -n {fna} -g {rg}")
            return OK
        if az_run(["functionapp", "restart", "-n", fna, "-g", rg, *sub_args(o.env), "-o", "none"]) is None:
            eprint("restart failed (creds/PIM?).")
            return FAIL
        print(f"{fna}: restart requested.")
        return OK
    if o.verb in ("full", "park", "resume", "smoke"):
        g = guard_env(o, f"{o.verb} {o.env}")
        if g is not None:
            return g
        s = sub_for(o.env)
        if not s:
            eprint(f"no subscription id for '{o.env}' — set ARN_SUB.")
            return USAGE
        script = {"full": "deploy.sh", "park": "park.sh", "resume": "resume.sh", "smoke": "smoke-test.sh"}[o.verb]
        extra = [a for a in o.passthrough if a.startswith("--skip")]
        return delegate(o, ["bash", f"deployment/scripts/{script}", *extra],
                        {"SUBSCRIPTION": s, "ENVIRONMENT": o.env})
    return unknown(f"deploy {o.verb}")


def deploy_web(o):
    g = guard_env(o, "deploy web")
    if g is not None:
        return g
    rg, swa = ain_rg(o.env), swa_app(o.env)
    if o.dry_run:
        print("[dry-run] (cd web && npx tsc -b && npx vite build)")
        print(f"[dry-run] TOKEN=$(az staticwebapp secrets list -n {swa} -g {rg} --query properties.apiKey)")
        rbac = o.env in ("test", "prod")
        stage_cfg = (f"python3 deployment/static-web-app/render-rbac-config.py --env {o.env} "
                     f"$stage/staticwebapp.config.json" if rbac
                     else "cp web/public/staticwebapp.config.json $stage/staticwebapp.config.json")
        print(f"[dry-run] stage=/tmp/arn_swa_{o.env}_$(date +%s); cp -a web/dist $stage/dist; "
              f"{stage_cfg} (and copied into $stage/dist/)")
        print("[dry-run] cd $stage && npx @azure/static-web-apps-cli deploy ./dist "
              "--deployment-token *** --env production   # cwd carries the intended auth config")
        return OK
    rc = bash(o, "-lc", "cd web && npx tsc -b && npx vite build")
    if rc != OK:
        eprint("web build failed.")
        return rc
    # The SWA CLI resolves `staticwebapp.config.json` from its WORKING DIRECTORY, not from the
    # artifact folder, and `delegate` runs everything from the repo root — where
    # `web/public/staticwebapp.config.json` (the dev built-in-provider config) sits. Overlaying
    # the artifact alone is NOT enough: the CLI announces the repo-root config and applies it,
    # silently reverting test/prod to role-less auth (bit prod 2026-07-07, again 2026-07-16,
    # and observed live on test 2026-08-12 — "Found configuration file: web/public/…").
    # So deploy from a staging directory whose ROOT config is the one this environment must
    # get; the same file goes inside the artifact, so both resolution orders agree.
    stage = f"/tmp/arn_swa_{o.env}_{int(time.time())}"
    # test/prod go through the renderer, which injects THIS environment's rolesSource token
    # audience (`<clientId>/.default`). Copying the canonical config instead would ship
    # OIDC-only scopes, Entra would mint a Graph-audience token, and every sign-in would be
    # discarded unvalidated — the 2026-08-13 "empty Operators page" fault.
    if o.env in ("test", "prod"):
        stage_cfg = (f"python3 deployment/static-web-app/render-rbac-config.py --env {o.env} "
                     f"{stage}/staticwebapp.config.json")
    else:
        stage_cfg = f"cp web/public/staticwebapp.config.json {stage}/staticwebapp.config.json"
    rc = bash(o, "-lc",
              f"rm -rf {stage} && mkdir -p {stage} && cp -a web/dist {stage}/dist "
              f"&& {stage_cfg} "
              f"&& cp {stage}/staticwebapp.config.json {stage}/dist/staticwebapp.config.json")
    if rc != OK:
        eprint("failed to stage the SWA artifact + auth config.")
        return rc
    provider = "prod-style RBAC (custom Entra provider + /api/GetRoles)" \
        if o.env in ("test", "prod") else "built-in provider"
    print(f"staged SWA artifact at {stage} with {provider} auth config ({o.env}).")
    tok = ""
    for attempt in range(5):
        tok = (az_run(["staticwebapp", "secrets", "list", "-n", swa, "-g", rg, *sub_args(o.env),
                       "--query", "properties.apiKey", "-o", "tsv"]) or "").strip()
        if tok:
            break
        if attempt == 0:
            _remint_arm_token()  # common cause: ARM token predating a PIM activation
        if attempt < 4:
            eprint(f"SWA token fetch failed (attempt {attempt + 1}/5) — retrying in 15s…")
            time.sleep(15)
    if not tok:
        eprint("could not get SWA deployment token (creds/PIM?).")
        return INFRA
    return bash(o, "-lc",
                f"cd {stage} && npx --yes @azure/static-web-apps-cli deploy ./dist "
                '--deployment-token "$SWA_CLI_DEPLOYMENT_TOKEN" --env production',
                env={"SWA_CLI_DEPLOYMENT_TOKEN": tok})


def deploy_api(o):
    g = guard_env(o, "deploy api")
    if g is not None:
        return g
    rg, fna = ain_rg(o.env), fna_app(o.env)
    zippath = f"/tmp/arn_{int(time.time())}.zip"
    if o.dry_run:
        print("[dry-run] dotnet publish src/ArnBulletinGenerator.Functions -c Release -o publish --no-self-contained")
        print(f"[dry-run] (cd publish && zip -r {zippath} .) → functionapp deployment source config-zip -n {fna} -g {rg}")
        print(f"[dry-run] az functionapp restart -n {fna} -g {rg}; poll webapp state == Running")
        return OK
    steps = ("rm -rf publish && dotnet publish src/ArnBulletinGenerator.Functions/ArnBulletinGenerator.Functions.csproj "
             f"-c Release -o publish --no-self-contained && (cd publish && zip -r {zippath} . -q)")
    rc = bash(o, "-lc", steps)
    if rc != OK:
        eprint("publish/zip failed.")
        return rc
    sa = sub_args(o.env)
    if az_run(["functionapp", "deployment", "source", "config-zip", "-g", rg, "-n", fna, *sa,
               "--src", zippath, "-o", "none"]) is None:
        eprint("zip deploy failed (creds/PIM?).")
        try:
            os.remove(zippath)
        except OSError:
            pass
        return FAIL
    az_run(["functionapp", "restart", "-n", fna, "-g", rg, *sa, "-o", "none"])
    state = ""
    for _ in range(12):
        time.sleep(5)
        state = (az_run(["webapp", "show", "-n", fna, "-g", rg, *sa, "--query", "state", "-o", "tsv"]) or "").strip()
        if state == "Running":
            break
    try:
        os.remove(zippath)
    except OSError:
        pass
    print(f"{fna}: {state or 'unknown'} (allow ~1-2 min for the isolated worker to load new assemblies).")
    return OK if state == "Running" else FAIL


def deploy_all(o):
    """One-shot code deploy: api (Functions) first, then web (SWA) — the fresh UI never
    outruns the API it expects. PIM/token self-heal already ran in cmd_deploy's preflight."""
    g = guard_env(o, "deploy all")
    if g is not None:
        return g
    done = []
    if not o.has("skip-api"):
        rc = deploy_api(o)
        if rc != OK:
            eprint("deploy all: api failed — stopping before web.")
            return rc
        done.append("api")
    if not o.has("skip-web"):
        rc = deploy_web(o)
        if rc != OK:
            return rc
        done.append("web")
    if not done:
        eprint("deploy all: nothing to do (--skip-web and --skip-api).")
        return USAGE
    print(f"deploy all ({o.env}): {' + '.join(done)} deployed.")
    return OK


# ──────────────────────────── job delete / purge ────────────────────────────
def _jobs_base(env):
    return f'{resolve_env(env)["functionApp"]}/api/jobs'


def _job_etag(env, jid, tok):
    """DELETE needs the job's revision token, so load the row and read its ETag."""
    _, d = http("GET", f"{_jobs_base(env)}/{jid}", tok)
    if not isinstance(d, dict):
        return None
    return next((d[k] for k in ("ETag", "Etag", "etag") if d.get(k)), None)


def _job_delete_one(env, jid, tok, etag=None):
    """DELETE /jobs/{id} is guarded by optimistic concurrency: it 428s without a
    matching ExpectedEtag, and the token goes in the BODY, not an If-Match header.
    A caller-supplied etag (from a list row) is tried first; on any non-2xx we
    reload and retry once, which covers a row that changed since the listing."""
    if etag:
        st, _ = http("DELETE", f"{_jobs_base(env)}/{jid}", tok, body={"ExpectedEtag": etag})
        if st and 200 <= st < 300:
            return st
    fresh = _job_etag(env, jid, tok)
    if not fresh:
        return st if etag else None
    st, _ = http("DELETE", f"{_jobs_base(env)}/{jid}", tok, body={"ExpectedEtag": fresh})
    return st


def job_delete(o, env, jid):
    if o.dry_run:
        print(f"[dry-run] DELETE {_jobs_base(env)}/{jid}")
        return OK
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — `az login`.")
        return INFRA
    status = _job_delete_one(env, jid, tok)
    ok = bool(status and 200 <= status < 300)
    print(f"delete {jid[:8]}: {status}")
    return OK if ok else FAIL


def job_purge(o, env):
    tok = az_token("https://management.azure.com")
    if not tok:
        eprint("no management token — `az login`.")
        return INFRA
    _, resp = http("GET", _jobs_base(env), tok)
    rows = resp.get("Items", []) if isinstance(resp, dict) else []
    ids = [(j.get("JobId"), j.get("Etag")) for j in rows if j.get("JobId")]
    if o.dry_run:
        print(f"[dry-run] DELETE {len(ids)} job(s) on {env}")
        return OK
    if not ids:
        print("no jobs to purge.")
        return OK
    rc, done = OK, 0
    for jid, etag in ids:
        st = _job_delete_one(env, jid, tok, etag)
        if st and 200 <= st < 300:
            done += 1
        else:
            rc = FAIL
            eprint(f"  delete {jid[:8]} failed: {st}")
    print(f"purged {done} of {len(ids)} job(s) on {env} (rc {rc}).")
    return rc


# ──────────────────────────── ci (Azure DevOps) ────────────────────────────
def _ado_token():
    return (az_run(["account", "get-access-token", "--resource", ADO_RESOURCE, "--tenant", ADO_TENANT,
                    "--query", "accessToken", "-o", "tsv"]) or "").strip() or None


def _ado_pat():
    """PAT from env, else ~/.config/arn/ado.pat (chmod 600, outside the repo)."""
    p = os.environ.get("AZURE_DEVOPS_EXT_PAT") or os.environ.get("ADO_PAT")
    if p:
        return p.strip()
    try:
        return (Path.home() / ".config" / "arn" / "ado.pat").read_text().strip() or None
    except OSError:
        return None


def _ado_auth():
    """ADO Authorization header: a PAT (Basic) if available, else the CLI bearer token."""
    pat = _ado_pat()
    if pat:
        return "Basic " + base64.b64encode(f":{pat}".encode()).decode()
    tok = _ado_token()
    return f"Bearer {tok}" if tok else None


def _ado_err(st, body):
    """Return an error string if the ADO response is unusable, else None."""
    if st and 200 <= st < 300 and isinstance(body, dict):
        return None
    msg = body.get("message") if isinstance(body, dict) else str(body)[:300]
    return f"ADO {st}: {msg}"


def cmd_ci(o):
    if o.help or not o.verb:
        return group_help("ci", [("run", "queue pipeline 108 on master"),
                                 ("watch", "<id> poll until the run completes"),
                                 ("logs", "<id> failed tasks + their logs"), ("status", "recent runs")])
    base = f"{ADO_ORG}/{ADO_PROJECT}/_apis"
    if o.verb == "run" and o.dry_run:
        print(f"[dry-run] POST {base}/build/builds  (definition {ADO_PIPELINE}, refs/heads/master)")
        return OK
    auth = _ado_auth()
    if not auth:
        eprint("no Azure DevOps auth — set AZURE_DEVOPS_EXT_PAT (a PAT) or az login as the ARN identity.")
        return INFRA
    if o.verb == "run":
        st, body = http("POST", f"{base}/build/builds?api-version=7.1", auth=auth,
                        body={"definition": {"id": ADO_PIPELINE}, "sourceBranch": "refs/heads/master"})
        if not st or st >= 300:
            eprint(f"queue failed ({st}): {body}")
            return FAIL
        bid = body.get("id") if isinstance(body, dict) else None
        web = (body.get("_links", {}) or {}).get("web", {}).get("href", "") if isinstance(body, dict) else ""
        print(f"queued build {bid}  {web}")
        if o.json:
            out_json(body)
        return OK
    if o.verb == "status":
        st, body = http("GET", f"{base}/build/builds?definitions={ADO_PIPELINE}&$top=10&api-version=7.1", auth=auth)
        err = _ado_err(st, body)
        if err:
            eprint(err)
            return INFRA
        runs = body.get("value", [])
        if o.json:
            out_json(runs)
            return OK
        for r in runs:
            print(f"  {r.get('id')}  {str(r.get('status', '')):<12} {str(r.get('result', '') or '-'):<12} "
                  f"{(r.get('sourceBranch') or '').replace('refs/heads/', '')}  {str(r.get('queueTime', ''))[:19]}")
        return OK
    bid = o.pos(0)
    if not bid:
        return need("ci", o.verb)
    if o.verb == "watch":
        while True:
            st, body = http("GET", f"{base}/build/builds/{bid}?api-version=7.1", auth=auth)
            err = _ado_err(st, body)
            if err:
                eprint(err)
                return INFRA
            status_s, result = body.get("status"), body.get("result")
            print(f"  build {bid}: {status_s} {result or ''}", flush=True)
            if status_s == "completed":
                return OK if result == "succeeded" else FAIL
            time.sleep(15)
    if o.verb == "logs":
        st, tl = http("GET", f"{base}/build/builds/{bid}/timeline?api-version=7.1-preview.2", auth=auth)
        err = _ado_err(st, tl)
        if err:
            eprint(err)
            return INFRA
        recs = tl.get("records", [])
        failed = [r for r in recs if r.get("result") in ("failed", "canceled")]
        if not failed:
            print(f"no failed tasks on build {bid} (see `arn ci status`).")
            return OK
        for r in failed:
            print(f"\n=== {r.get('type')} · {r.get('name')} · {r.get('result')} ===")
            for issue in (r.get("issues") or []):
                print(f"  {issue.get('type')}: {issue.get('message')}")
            log = (r.get("log") or {}).get("id")
            if log:
                _, ltext = http("GET", f"{base}/build/builds/{bid}/logs/{log}?api-version=7.1", auth=auth)
                print("\n".join(str(ltext).splitlines()[-40:]))
        return FAIL
    return unknown(f"ci {o.verb}")


# ──────────────────────────── foundry ────────────────────────────
def cmd_foundry(o):
    if o.help or not o.verb:
        return group_help("foundry", [("agents", "list role agents + model + version"),
                                      ("agent", "<id> full definition of one agent"),
                                      ("provision", "(re)create the role agents [provision-foundry-agents.sh]")])
    if o.env == "local":
        eprint("foundry is a real-service group (no local).")
        return USAGE
    ep = resolve_env(o.env)["foundryProject"]
    if o.verb == "agents":
        data = az_json(["rest", "--method", "get", "--resource", FOUNDRY_RESOURCE,
                        "--url", f"{ep}/agents?api-version={FOUNDRY_API}",
                        "--query", "data[].{id:id, model:versions.latest.definition.model, version:versions.latest.version}",
                        "-o", "json"])
        if data is None:
            eprint("could not list Foundry agents (creds/endpoint?).")
            return INFRA
        if o.json:
            out_json(data)
        else:
            for a in data:
                print(f"  {str(a.get('id', '')):<30} {str(a.get('model', '')):<18} v{a.get('version', '')}")
        return OK
    if o.verb == "agent":
        aid = o.pos(0)
        if not aid:
            return need("foundry", "agent")
        data = az_json(["rest", "--method", "get", "--resource", FOUNDRY_RESOURCE,
                        "--url", f"{ep}/agents/{aid}?api-version={FOUNDRY_API}"])
        if data is None:
            eprint(f"agent '{aid}' not found.")
            return FAIL
        out_json(data)
        return OK
    if o.verb == "provision":
        g = guard_env(o, "provision agents")
        if g is not None:
            return g
        return delegate(o, ["bash", "deployment/scripts/provision-foundry-agents.sh", ep], {"PROJECT_ENDPOINT": ep})
    return unknown(f"foundry {o.verb}")


# ──────────────────────────── helpers ────────────────────────────
def need(group, verb):
    eprint(f"usage: arn {group} {verb} <id>")
    return USAGE


def unknown(what):
    eprint(f"unknown command: {what}. Run `arn` for the command tree.")
    return USAGE


def group_help(group, verbs):
    print(f"arn {group} — verbs:")
    for v, desc in verbs:
        print(f"  {v:<12} {desc}")
    print("\nglobal: --env local|dev|test|prod   --json   --dry-run   -h/--help")
    return OK


def root_help():
    print(
        "arn — master testing toolkit for Arn bulletins (LLM-first).\n"
        "usage: arn <group> <verb> [args] [--env local|dev|test|prod] [--json] [--dry-run]\n\n"
        "groups (native = in-process Python; orchestrated = clean flags → existing engine):\n"
        "  env         show | check | up | down                                  [native]\n"
        "  fixtures    list | show | capture <job> --label L                     [native + capture-regression.sh]\n"
        "  run         eval | regression | fallback | matrix                      [eval.sh / dotnet test]\n"
        "  trigger     --market --duration [--via post|poller] [--wait]           [native POST / arm-schedule+watch / test-bulletin.sh]\n"
        "  job         get|logs|traces|captures|list|approve|publish|regenerate|audio|watch  [native]\n"
        "  perf        load --scenario … | profile                               [test/load/run.sh / dotnet test]\n"
        "  safety      check <script> | run                                      [native → deployed arn-safety]\n"
        "  report      scorecard [name] | compare <a> <b>                        [native]\n"
        "  coverage    show | unknown [--market M] [--since ISO]                    [native GET /api/coverage]\n"
        "  deploy      all | web | api | restart | full | park | resume | smoke  [native + deploy.sh]\n"
        "  settings    get | set <k> <v> [--no-restart]                           [native — Function App config]\n"
        "  logs        [--job <id>] [--minutes N] [--grep T]                      [native — Log Analytics]\n"
        "  pim         status | activate | wait                                   [native — PIM self-activate]\n"
        "  ci          run | watch <id> | logs <id> | status                     [native — Azure DevOps 108]\n"
        "  foundry     agents | agent <id> | provision                         [native + provision script]\n"
        "  health      GET /api/health                                            [native]\n"
        "  config      export | import <f> [--apply] | pending | approve | reject | schema   [native]\n\n"
        "exit codes: 0 ok · 1 failure · 2 usage · 3 infra/creds")
    return OK




class Opts:
    def __init__(self):
        self.group = self.verb = None
        self.env = "local"
        self.json = self.dry_run = self.help = False
        self._flags, self._pos, self.passthrough = {}, [], []

    def flag(self, n):
        return self._flags.get(n)

    def has(self, n):
        return n in self._flags

    def pos(self, i):
        return self._pos[i] if i < len(self._pos) else None


def parse(argv):
    o = Opts()
    gset = vset = False
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in ("--help", "-h"):
            o.help = True
        elif a == "--json":
            o.json = True
        elif a == "--dry-run":
            o.dry_run = True
        elif a == "--env":
            if i + 1 < len(argv):
                i += 1
                o.env = argv[i]
        elif not gset and not a.startswith("--"):
            o.group, gset = a, True
        elif gset and not vset and not a.startswith("--"):
            o.verb, vset = a, True
        else:
            o.passthrough.append(a)
            if a.startswith("--"):
                key, val = a[2:], None
                if i + 1 < len(argv) and not argv[i + 1].startswith("--"):
                    i += 1
                    val = argv[i]
                    o.passthrough.append(val)
                o._flags[key] = val
            else:
                o._pos.append(a)
        i += 1
    return o


DISPATCH = {
    "env": cmd_env, "fixtures": cmd_fixtures, "run": cmd_run,
    "trigger": cmd_trigger, "job": cmd_job, "perf": cmd_perf,
    "safety": cmd_safety, "report": cmd_report, "coverage": cmd_coverage,
    "deploy": cmd_deploy, "settings": cmd_settings, "logs": cmd_logs, "pim": cmd_pim,
    "ci": cmd_ci, "foundry": cmd_foundry, "health": cmd_health, "config": cmd_config,
}


def main(argv):
    o = parse(argv)
    if ROOT is None:
        eprint("could not locate repo root (ArnBulletinGenerator.slnx).")
        return INFRA
    if o.group in (None, "help", "--help", "-h"):
        return root_help()
    fn = DISPATCH.get(o.group)
    try:
        return fn(o) if fn else unknown(o.group)
    except AzError as err:
        eprint(str(err))
        return INFRA


if __name__ == "__main__":
    # Behave like a normal Unix tool when piped into head/grep (no BrokenPipe traceback).
    try:
        signal.signal(signal.SIGPIPE, signal.SIG_DFL)
    except (AttributeError, ValueError):
        pass
    try:
        sys.exit(main(sys.argv[1:]))
    except BrokenPipeError:
        sys.exit(OK)
    except KeyboardInterrupt:
        sys.exit(130)
