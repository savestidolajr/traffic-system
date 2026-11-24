#!/usr/bin/env python3
"""
Export a job's full prompt/response timeline as an LLM-readable JSON file plus a
companion human HTML viewer. Works against a deployed environment OR the local dev
stack (trigger a fresh bulletin, capture it, export it — the full iteration loop).

Usage — deployed job:
  python3 scripts/export-prompts.py <job-id-or-url>                  # full markdown summary to stdout
  python3 scripts/export-prompts.py <job-id-or-url> --format json    # full JSON to stdout (for jq)
  python3 scripts/export-prompts.py <job-id-or-url> --list-captures  # compact capture table
  python3 scripts/export-prompts.py <job-id-or-url> --show 3         # dump one raw capture (prompt+response)
  python3 scripts/export-prompts.py <job-id-or-url> --env prod --refresh --quiet

Usage — local dev stack (requires ./scripts/local-start.sh already running):
  python3 scripts/export-prompts.py --local                              # trigger+export a fresh Sydney 30s bulletin
  python3 scripts/export-prompts.py --local --market melbourne --duration 45
  python3 scripts/export-prompts.py --local <local-job-id>               # inspect an existing local job, no trigger

Usage — prompt-iteration loop (the point of --local):
  python3 scripts/export-prompts.py --local                              # 1. baseline run
  # edit prompts/draft.md (or review.md/refine-sentence.md/system.md/...) by hand
  python3 scripts/export-prompts.py --local --restart-host --compare previous   # 2. re-run + auto-diff
  # repeat step 2 until satisfied

Usage — A/B an experimental prompt SET without touching the tracked prompts/ tree:
  cp -r prompts /tmp/variant-a && $EDITOR /tmp/variant-a/draft.md
  python3 scripts/export-prompts.py --local --prompts-dir /tmp/variant-a --compare previous

Output in <out>/ (default ./job-diagnostics/<jobId>/, or ./job-diagnostics/local/<jobId>/ with --local):
  prompt-export.json  — everything, structured for LLM consumption (read `_guide` first)
  prompt-viewer.html  — self-contained human viewer (embeds the JSON, no server needed)
  SUMMARY.md          — the same markdown digest printed to stdout by default
  COMPARE.md/.json    — present only when --compare was given: this run vs the baseline

Entry point for LLM/agent use: run with no flags and read stdout. It already contains
the diagnosis (fallback state, review-round verdicts, issues raised, script word-count
evolution, safety result, warnings) — most debugging sessions never need to open the
JSON at all. Use `--show N` to pull one specific raw prompt/response when the summary
points at a suspicious capture index. Use `--compare previous` after a local re-run to
see exactly what changed, word-for-word, without re-reading the whole export.
"""

from __future__ import annotations

import argparse
import base64
import difflib
import gzip
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

SUBSCRIPTION_ID = "5e5f8695-2207-4572-8e80-1065a1b0c135"  # "IBP Sub"
ENV_STORAGE = {
    "dev": "ibpauedevstaapp01",
    "test": "ibpauetststaapp01",
    "prod": "ibpaueprdstaapp01",
}
TABLE_NAME = "radioJobs"
CAPTURE_CONTAINER = "llm-call-captures"

JOB_COLUMNS = [
    "RowKey", "Status", "Market", "Station", "TargetDurationSeconds",
    "ScriptText", "LogsJson",
    "IncidentsJson", "AllIncidentsJson", "CorridorsJson", "FlowRoadsJson",
    "GeneratorInputJson",
    "WeatherJson", "FuelJson", "ContentSelectionJson", "GoodRunRoadsJson", "MonitoredRoutesJson",
    "FallbackSourceJobId", "FallbackStreak", "FallbackReason", "FallbackAppliedAt",
    "AudioUrl", "ScheduledForUtc", "CreatedAt", "LastUpdatedAt",
]

JOB_ID_RE = re.compile(r"^[0-9a-fA-F]{16,}$")

# ─── local dev stack (mirrors scripts/local-start.sh / test-bulletin.sh) ────
REPO_ROOT = Path(__file__).resolve().parent.parent
LOCAL_SETTINGS_PATH = REPO_ROOT / "src/ArnBulletinGenerator.Functions/local.settings.json"
LOCAL_START_SCRIPT = REPO_ROOT / "scripts/local-start.sh"
AZURITE_CONNECTION_STRING = (
    "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
    "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
    "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
    "QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;"
    "TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"
)
LOCAL_API_BASE = "http://localhost:7071/api"
# Mock Admin principal for the local Functions host (AadClientId="local-dev"), identical to
# the one baked into scripts/test-bulletin.sh / scripts/local-start.sh.
LOCAL_PRINCIPAL = (
    "eyJhdXRoX3R5cCI6ImFhZCIsIm5hbWVfdHlwIjoibmFtZSIsInJvbGVfdHlwIjoicm9sZXMiLCJjbGFpbXMiOlt7InR5cCI6"
    "Im5hbWUiLCJ2YWwiOiJMb2NhbCBEZXYifSx7InR5cCI6InJvbGVzIiwidmFsIjoiQWRtaW4ifSx7InR5cCI6Imh0dHA6Ly9z"
    "Y2hlbWFzLm1pY3Jvc29mdC5jb20vaWRlbnRpdHkvY2xhaW1zL29iamVjdGlkZW50aWZpZXIiLCJ2YWwiOiIwMDAwMDAwMC0w"
    "MDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifV19"
)
MARKET_PRESETS = {
    "sydney":    {"lat": -33.8688, "lng": 151.2093, "radius": 25, "station": "2GB"},
    "melbourne": {"lat": -37.8136, "lng": 144.9631, "radius": 25, "station": "3AW"},
    "brisbane":  {"lat": -27.4698, "lng": 153.0251, "radius": 25, "station": "4BC"},
    "perth":     {"lat": -31.9505, "lng": 115.8605, "radius": 25, "station": "6PR"},
    "adelaide":  {"lat": -34.9285, "lng": 138.6007, "radius": 25, "station": "5AA"},
    "goldcoast": {"lat": -28.0167, "lng": 153.4000, "radius": 20, "station": "Gold FM"},
}
LOCAL_RUNS_MANIFEST = Path("job-diagnostics") / "local" / ".runs.json"


# ─── low-level helpers ──────────────────────────────────────────────────────

def die(msg: str) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def extract_job_id(raw: str) -> str:
    raw = raw.strip()
    if JOB_ID_RE.match(raw):
        return raw
    tail = raw.rstrip("/").split("/")[-1].split("?")[0].split("#")[0]
    if JOB_ID_RE.match(tail):
        return tail
    die(f"Cannot extract job id from '{raw}'. Pass a bare job id or a /jobs/<id> URL.")


def check_az_available() -> None:
    if shutil.which("az") is None:
        die("Azure CLI ('az') not found on PATH. Install it or run from a shell that has it.")


def run_az(args: list[str], check: bool = True) -> str:
    """Runs `az <args>`. With check=False, failures return "" instead of dying —
    used for cross-environment job probing where a miss is expected, not fatal."""
    result = subprocess.run(["az", *args], capture_output=True, text=True)
    if result.returncode != 0:
        if check:
            die(f"az {' '.join(args[:2])} failed:\n{result.stderr.strip()}")
        return ""
    return result.stdout


def check_subscription() -> None:
    sid = run_az(["account", "show", "--query", "id", "-o", "tsv"]).strip()
    if sid != SUBSCRIPTION_ID:
        die(
            f"Wrong subscription. Expected '{SUBSCRIPTION_ID}' (IBP Sub), "
            f"got '{sid}'. Run: az account set --subscription \"IBP Sub\""
        )


# ─── data fetch ─────────────────────────────────────────────────────────────

def conn_args_for_account(account: str) -> list[str]:
    return ["--account-name", account, "--auth-mode", "login"]


def conn_args_for_local() -> list[str]:
    """Azurite emulator, connection-string auth — no `az login` needed at all."""
    return ["--connection-string", AZURITE_CONNECTION_STRING]


def expand_gz_columns(job: dict) -> dict:
    """The backend stores an oversized JSON column as "gz:" + base64(gzip(json))
    (TableStorageJobRepository.FitTableProperty), so a big job arrives with, e.g.,
    GeneratorInputJson still packed. Expanding here keeps every consumer below —
    evidence rendering AND the regression-fixture builder — seeing plain JSON.
    Without it the packed columns silently read as unparseable: goldcoast job
    de2d1b60 (2026-09-03) logged a 53,453-char GeneratorInputJson, exported
    `generatorInput: null`, and failed --build-fixture in the C# reader with
    "'g' is an invalid start of a value"."""
    for key, value in list(job.items()):
        if isinstance(value, str) and value.startswith("gz:"):
            try:
                job[key] = gzip.decompress(base64.b64decode(value[3:])).decode("utf-8")
            except (ValueError, OSError) as error:
                print(f"Warning: {key} is gz-packed but would not expand ({error}); "
                      "leaving the raw value.", file=sys.stderr)
    return job


def fetch_job(conn_args: list[str], job_id: str) -> dict | None:
    out = run_az([
        "storage", "entity", "query",
        *conn_args,
        "--table-name", TABLE_NAME,
        # RowKey alone also matches the JobIndex stub row (PK=JobIndex, no snapshot
        # columns) since the Job_{market} partition change; exclude it for the data row.
        "--filter", f"RowKey eq '{job_id}' and PartitionKey ne 'JobIndex'",
        "--select", *JOB_COLUMNS,
        "-o", "json",
    ], check=False)
    if not out:
        return None
    try:
        data = json.loads(out)
    except json.JSONDecodeError:
        return None
    items = data.get("items", [])
    return expand_gz_columns(items[0]) if items else None


def find_job(job_id: str, explicit_env: str | None) -> tuple[str, dict]:
    """Explicit --env: try only that one (clear error on miss). No --env: probe
    dev, test, prod in order — jobs are almost always in dev, but this avoids a
    round-trip when someone pastes a prod/test operator-console URL."""
    envs_to_try = [explicit_env] if explicit_env else list(ENV_STORAGE.keys())
    tried = []
    for env in envs_to_try:
        job = fetch_job(conn_args_for_account(ENV_STORAGE[env]), job_id)
        tried.append(env)
        if job:
            if not explicit_env and env != envs_to_try[0]:
                print(f"Note: not found in {envs_to_try[0]}, found in {env}.", file=sys.stderr)
            return env, job
    die(f"Job {job_id} not found in: {', '.join(tried)}.")


def parse_logs(job: dict) -> list[dict]:
    try:
        return json.loads(job.get("LogsJson", "[]") or "[]")
    except (json.JSONDecodeError, TypeError):
        return []


def list_llm_captures(conn_args: list[str], job_id: str) -> list[dict]:
    result = subprocess.run([
        "az", "storage", "blob", "list",
        *conn_args,
        "--container-name", CAPTURE_CONTAINER,
        "--prefix", f"{job_id}/",
        "--query", "[].{name:name}",
        "-o", "json",
    ], capture_output=True, text=True)
    if result.returncode != 0:
        return []
    try:
        return json.loads(result.stdout) or []
    except json.JSONDecodeError:
        return []


def download_llm_captures(conn_args: list[str], job_id: str, dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    subprocess.run([
        "az", "storage", "blob", "download-batch",
        *conn_args,
        "--source", CAPTURE_CONTAINER,
        "--pattern", f"{job_id}/*",
        "--destination", str(dest),
    ], capture_output=True, text=True)
    nested = dest / job_id
    if nested.is_dir():
        for f in nested.iterdir():
            f.rename(dest / f.name)
        nested.rmdir()


def ensure_captures(conn_args: list[str], job_id: str, out_dir: Path, refresh: bool) -> tuple[list[Path], int, list[str]]:
    """Downloads (if needed) and returns (local files, blob-listed count, warnings).
    Re-downloads automatically when the local file count doesn't match the blob
    listing — catches a prior interrupted/partial download instead of silently
    serving stale/incomplete data."""
    warnings: list[str] = []
    captures_dir = out_dir / "llm_calls"
    blob_list = list_llm_captures(conn_args, job_id)
    expected = len(blob_list)

    need_download = refresh or not captures_dir.is_dir()
    if not need_download and expected > 0:
        existing = list(captures_dir.glob("*.json"))
        if len(existing) != expected:
            need_download = True
            warnings.append(
                f"Local capture count ({len(existing)}) didn't match blob listing "
                f"({expected}); re-downloaded."
            )

    if need_download and expected > 0:
        download_llm_captures(conn_args, job_id, captures_dir)

    files = sorted(captures_dir.glob("*.json")) if captures_dir.is_dir() else []
    if expected > 0 and len(files) != expected:
        warnings.append(
            f"Downloaded {len(files)} capture file(s) but blob listing showed "
            f"{expected}; some may have failed to download."
        )
    return files, expected, warnings


def walk_fallback_chain(conn_args: list[str], start_job_id: str) -> list[dict]:
    chain = []
    cur, seen = start_job_id, set()
    while cur and cur not in seen:
        seen.add(cur)
        out = run_az([
            "storage", "entity", "query",
            *conn_args,
            "--table-name", TABLE_NAME,
            # Exclude the JobIndex stub row (PK=JobIndex) — see fetch_job.
            "--filter", f"RowKey eq '{cur}' and PartitionKey ne 'JobIndex'",
            "--select", "RowKey", "Status", "FallbackReason", "FallbackSourceJobId",
            "FallbackStreak", "ScheduledForUtc",
            "-o", "json",
        ], check=False)
        if not out:
            break
        try:
            items = json.loads(out).get("items", [])
        except json.JSONDecodeError:
            break
        if not items:
            break
        j = items[0]
        chain.append({
            "jobId": j.get("RowKey"),
            "status": j.get("Status"),
            "fallbackReason": j.get("FallbackReason"),
            "fallbackStreak": j.get("FallbackStreak"),
            "scheduledForUtc": j.get("ScheduledForUtc"),
        })
        cur = j.get("FallbackSourceJobId")
    return chain


# ─── local dev stack: trigger, host lifecycle, prompt-variant override ─────

def local_api(method: str, path: str, body: dict | None = None, timeout: int = 20) -> dict:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(
        f"{LOCAL_API_BASE}{path}", data=data, method=method,
        headers={"X-MS-CLIENT-PRINCIPAL": LOCAL_PRINCIPAL, "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", errors="replace")
        die(f"{method} {path} failed: HTTP {e.code} {detail[:400]}")
    except urllib.error.URLError as e:
        die(f"{method} {path} failed: {e.reason}. Is the local host running (./scripts/local-start.sh)?")


def check_local_host() -> bool:
    try:
        with urllib.request.urlopen(f"{LOCAL_API_BASE}/health", timeout=5) as resp:
            return resp.status in (200, 503)
    except urllib.error.HTTPError as e:
        return e.code in (200, 503)
    except (urllib.error.URLError, OSError):
        return False


def ensure_capture_setting(max_captured_jobs: int = 50) -> bool:
    """Enables Debug:CaptureLlmCalls in local.settings.json if not already on.
    Returns True iff the file was just changed (caller must restart the host —
    prompts/settings are static-loaded once at process start)."""
    if not LOCAL_SETTINGS_PATH.exists():
        die(f"{LOCAL_SETTINGS_PATH} not found — is this the Arn repo?")
    data = json.loads(LOCAL_SETTINGS_PATH.read_text("utf-8"))
    values = data.setdefault("Values", {})
    changed = False
    if str(values.get("Debug__CaptureLlmCalls", "")).lower() != "true":
        values["Debug__CaptureLlmCalls"] = "true"
        changed = True
    if "Debug__MaxCapturedJobs" not in values:
        values["Debug__MaxCapturedJobs"] = str(max_captured_jobs)
        changed = True
    if changed:
        LOCAL_SETTINGS_PATH.write_text(json.dumps(data, indent=2) + "\n", "utf-8")
    return changed


def restart_local_host(prompts_dir: str | None) -> None:
    """Force-kills any running func host (the documented procedure — a plain
    restart request is not exposed) and re-runs local-start.sh, which is
    idempotent (Azurite/queues/containers are left alone if already present).
    `prompts_dir`, if given, is exported as ARN_PROMPTS_DIR so the freshly
    started process statically loads that prompt set instead of ./prompts/
    (see LlmScriptGenerator.LoadPrompt) — lets you A/B an experimental prompt
    variant without touching the tracked tree."""
    print("Restarting local Functions host...", file=sys.stderr)
    subprocess.run(["pkill", "-9", "-f", "func start"], capture_output=True)
    subprocess.run(["pkill", "-9", "-f", "ArnBulletinGenerator.Functions.dll"], capture_output=True)
    time.sleep(2)

    env = dict(os.environ)
    if prompts_dir:
        resolved = str(Path(prompts_dir).resolve())
        for name in ("system", "draft", "review", "refine-sentence", "attribute", "safety", "director", "citation-fix"):
            if not (Path(resolved) / f"{name}.md").exists():
                die(f"--prompts-dir {resolved} is missing {name}.md — ARN_PROMPTS_DIR requires the complete prompt set.")
        env["ARN_PROMPTS_DIR"] = resolved
        print(f"  ARN_PROMPTS_DIR={resolved}", file=sys.stderr)
    else:
        env.pop("ARN_PROMPTS_DIR", None)

    if not LOCAL_START_SCRIPT.exists():
        die(f"{LOCAL_START_SCRIPT} not found.")
    log_path = Path("/tmp/arn-local-start.log")
    with open(log_path, "w") as log:
        subprocess.Popen(
            [str(LOCAL_START_SCRIPT)], cwd=str(REPO_ROOT), env=env,
            stdout=log, stderr=subprocess.STDOUT, start_new_session=True,
        )

    deadline = time.time() + 120
    while time.time() < deadline:
        if check_local_host():
            print("Host is up.", file=sys.stderr)
            return
        time.sleep(3)
    die(f"Local host did not become healthy within 120s after restart. Check /tmp/arn-func.log and {log_path}.")


def trigger_local_bulletin(market: str, duration: int, station: str | None,
                            voice_id: str | None, voice_name: str | None,
                            lat: float | None, lng: float | None, radius: float | None,
                            script_only: bool) -> str:
    preset = MARKET_PRESETS.get(market, {})
    resolved_lat = lat if lat is not None else preset.get("lat")
    resolved_lng = lng if lng is not None else preset.get("lng")
    resolved_radius = radius if radius is not None else preset.get("radius", 25)
    if resolved_lat is None or resolved_lng is None:
        die(f"Unknown market '{market}'. Use --lat/--lng/--radius, or one of: {', '.join(MARKET_PRESETS)}")

    schedule = local_api("GET", f"/schedules/{market}")
    voice_profile_id = schedule.get("VoiceProfileId") or schedule.get("voiceProfileId") if isinstance(schedule, dict) else None
    if not voice_profile_id:
        die(f"no voice: market '{market}' has no schedule; pass --voice-profile-id <id>")
    body: dict = {
        "location": station or preset.get("station", market),
        "targetDurationSeconds": duration,
        "coverage": {"latitude": resolved_lat, "longitude": resolved_lng, "radiusKm": resolved_radius},
        "scriptOnly": script_only,
        "VoiceProfileId": voice_profile_id,
    }
    if voice_id:
        body["voiceId"] = voice_id
    if voice_name:
        body["voiceName"] = voice_name

    result = local_api("POST", "/broadcast", body)
    job_id = result.get("JobId") or result.get("jobId")
    if not job_id:
        die(f"Broadcast trigger returned no job id: {result}")
    return job_id


def poll_local_job(job_id: str, timeout: int) -> dict:
    """Status casing has been observed to vary (e.g. 'APPROVED' on script-only
    local jobs vs 'WaitingApproval' on normal ones) — compare case-insensitively,
    mirroring test-bulletin.sh's own WaitingApproval|WAITING_APPROVAL regex."""
    terminal = {"waitingapproval", "failed", "fallbackused", "approved", "published", "rejected"}
    deadline = time.time() + timeout
    last: dict = {}
    last_status = None
    while time.time() < deadline:
        last = local_api("GET", f"/jobs/{job_id}")
        last_status = last.get("Status") or last.get("status")
        if str(last_status).lower() in terminal:
            return last
        time.sleep(3)
    die(f"Timeout after {timeout}s waiting for local job {job_id} (last status: {last_status})")


# ─── regression fixture: replay THIS job's exact original input under new code/prompts ─────

def build_fixture_input(job: dict, job_id: str, label: str, reason: str) -> dict:
    """The intermediate JSON handed to the C# fixture builder (JobFixtureReplayTests) — plain
    pass-through of the job's own raw column values, no transformation. The C# side prefers
    GeneratorInputJson when present (byte-identical) and otherwise reconstructs a
    GeneratorInputSnapshot from the independently-persisted Corridors/AllIncidents/FlowRoads/
    GoodRunRoads/MonitoredRoutes/Weather/Fuel/ContentSelection columns using the real domain
    types — deliberately NOT attempted in Python, since the JobCorridor/JobIncident -> writer
    TrafficCorridor/TrafficIncident mapping has real derived-field logic (e.g. incident Type is
    parsed back out of the persisted Headline) that only the actual C# types can get right."""
    return {
        "SourceJobId": job_id,
        "Label": label,
        "Reason": reason,
        "OriginalScript": job.get("ScriptText") or "",
        "CapturedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "TargetDurationSeconds": job.get("TargetDurationSeconds") or 30.0,
        "Station": job.get("Station") or "",
        "Market": job.get("Market") or "",
        "GeneratorInputJson": job.get("GeneratorInputJson"),
        "CorridorsJson": job.get("CorridorsJson"),
        "AllIncidentsJson": job.get("AllIncidentsJson"),
        "FlowRoadsJson": job.get("FlowRoadsJson"),
        "GoodRunRoadsJson": job.get("GoodRunRoadsJson"),
        "MonitoredRoutesJson": job.get("MonitoredRoutesJson"),
        "WeatherJson": job.get("WeatherJson"),
        "FuelJson": job.get("FuelJson"),
        "ContentSelectionJson": job.get("ContentSelectionJson"),
    }


def load_aoai_credentials() -> dict[str, str]:
    if not LOCAL_SETTINGS_PATH.exists():
        die(f"{LOCAL_SETTINGS_PATH} not found — cannot read Azure OpenAI credentials for a live replay.")
    values = json.loads(LOCAL_SETTINGS_PATH.read_text("utf-8")).get("Values", {})
    endpoint = values.get("AiSettings__AzureAI__Endpoint")
    api_key = values.get("AiSettings__AzureAI__ApiKey")
    model = values.get("AiSettings__AzureAI__Model")
    if not (endpoint and api_key and model):
        die("local.settings.json is missing AiSettings__AzureAI__{Endpoint,ApiKey,Model} — cannot replay live.")
    return {"AZURE_OPENAI_ENDPOINT": endpoint, "AZURE_OPENAI_API_KEY": api_key, "AZURE_OPENAI_MODEL": model}


def build_and_replay_fixture(job: dict, job_id: str, label: str, reason: str, replay: bool,
                              prompts_dir: str | None = None) -> dict | None:
    """Writes test/corpus/regressions/<label>.json from this job's data via the C# fixture
    builder (JobFixture_BuildAndReplay — real domain types, no hand-rolled JSON) and, if
    `replay`, replays it LIVE through the CURRENT prompts/code (a fresh `dotnet test` process
    reads prompts/*.md from disk at static-init, so an edit made since the flagged job ran is
    picked up automatically — no host restart needed, unlike --local). `prompts_dir` sets
    ARN_PROMPTS_DIR for this one replay only (same override --local --prompts-dir uses) so an
    experimental prompt variant can be tried without touching the tracked prompts/ tree. Returns
    the parsed replay result dict, or None when replay wasn't requested (the fixture is written
    either way).

    [SCOPE CAVEAT] This replays generator.GenerateScriptAsync + the shared review-refine-comply
    pass (RegressionEval.ReplayAsync) — the writer pipeline a system-prompt/guidance/formatter
    change actually lands in. It does NOT reproduce BroadcastOrchestrator's outer multi-attempt
    retry loop or its retry-exhausted finalization path (verified live: replaying job
    60ab1c05770c43898529c946eaff2a09 — whose ORIGINAL script had its fuel line silently dropped
    by that finalization path, per postLlmDelta — produced a REPLAY that kept the fuel line, since
    this harness never reaches that code path at all). Right tool for "does the model behave
    differently on this data with my prompt/code edit"; wrong tool for orchestrator-level
    retry/fallback bugs — replay the captured job through the full orchestrator for those."""
    fixture_input = build_fixture_input(job, job_id, label, reason)
    tmp_dir = Path(tempfile.mkdtemp(prefix="arn-fixture-"))
    input_path = tmp_dir / "input.json"
    output_path = tmp_dir / "result.json"
    input_path.write_text(json.dumps(fixture_input), "utf-8")

    env = dict(os.environ)
    env["ARN_FIXTURE_INPUT"] = str(input_path)
    env["ARN_FIXTURE_OUTPUT"] = str(output_path)
    if replay:
        env["ARN_EVAL_LIVE"] = "1"
        env.update(load_aoai_credentials())
    if prompts_dir:
        env["ARN_PROMPTS_DIR"] = str(Path(prompts_dir).resolve())

    print(f"Building{' and replaying' if replay else ''} regression fixture '{label}' "
          f"(dotnet build + test{', live Azure OpenAI calls' if replay else ''} — this can take a bit)"
          f"{f', prompts-dir={prompts_dir}' if prompts_dir else ''}...",
          file=sys.stderr)

    build = subprocess.run(
        ["dotnet", "build", "ArnBulletinGenerator.slnx", "-clp:ErrorsOnly", "-v", "q"],
        cwd=str(REPO_ROOT), capture_output=True, text=True,
    )
    if build.returncode != 0:
        die(f"dotnet build failed:\n{build.stdout}\n{build.stderr}")

    test = subprocess.run(
        ["dotnet", "test", "test/ArnBulletinGenerator.Infrastructure.Tests", "--no-build",
         "--filter", "JobFixture_BuildAndReplay", "-l", "console;verbosity=detailed"],
        cwd=str(REPO_ROOT), env=env, capture_output=True, text=True,
    )
    print(test.stdout[-4000:], file=sys.stderr)
    if test.returncode != 0:
        die(f"dotnet test failed (see output above). stderr tail:\n{test.stderr[-2000:]}")

    fixture_path = REPO_ROOT / "test" / "corpus" / "regressions" / f"{label}.json"
    print(f"Fixture written: {fixture_path}", file=sys.stderr)

    if not replay:
        return None
    if not output_path.exists():
        die("Replay ran but produced no result file — check the dotnet test output above.")
    return json.loads(output_path.read_text("utf-8"))


def render_fixture_replay_markdown(result: dict) -> str:
    original, replay = result["OriginalScript"], result["ReplayScript"]
    stats = word_diff_stats(original, replay)
    md = [f"# Fixture replay — {result['Label']}", ""]
    md.append(f"- Source job: `{result['SourceJobId']}`")
    md.append(f"- Corridors: {result['CorridorCount']}")
    md.append(f"- Fabrication: {'PASS' if result['FabricationPassed'] else 'FAIL \u2014 ' + '; '.join(result['FabricationIssues'])}")
    md.append(f"- Style: {'PASS' if result['StylePassed'] else 'FAIL \u2014 ' + '; '.join(result['StyleIssues'])}")
    md.append(f"- Safety: {'PASS' if result['SafetyPassed'] else 'FAIL \u2014 ' + '; '.join(result['SafetyIssues'])}")
    md.append("")
    md.append("## Original (flagged)")
    md.append(f"> {original}")
    md.append("")
    md.append("## Replay (current prompts/code, same frozen input)")
    md.append(f"> {replay}")
    md.append("")
    md.append(f"## Word diff (original \u2192 replay) \u2014 similarity {stats['similarity']}, "
               f"+{stats['wordsAdded']}/-{stats['wordsRemoved']} words")
    md.append(f"`{word_diff_markup(original, replay)}`")
    md.append("")
    md.append(f"Fixture: `test/corpus/regressions/{result['Label']}.json` "
               f"(the same corpus the full replay harness runs \u2014 commit it to keep this "
               f"as a permanent regression case, or delete it if this was just exploratory).")
    return "\n".join(md) + "\n"


# ─── capture parsing ────────────────────────────────────────────────────────

def extract_response_text(resp_json: str) -> str:
    """Extract visible text from captured model responses.

    Handles the Responses API shape (reasoning items may precede the message item,
    and text lives under output[*].content[*].text) plus the older Chat Completions
    shape. Keep this in sync with the prompt-viewer fallback: semantic parsing must
    see the same JSON verdict text the browser card displays.
    """
    try:
        obj = json.loads(resp_json)
    except (json.JSONDecodeError, TypeError):
        # Not an envelope. The conversational composer answers in prose, so its captured body
        # IS the read: returning "" here reported every compose as zero words.
        return resp_json if isinstance(resp_json, str) else ""

    if isinstance(obj, dict) and "output" in obj:
        parts: list[str] = []
        output = obj["output"]
        if isinstance(output, list):
            for item in output:
                if not isinstance(item, dict):
                    continue
                content = item.get("content", [])
                if not isinstance(content, list):
                    continue
                for chunk in content:
                    if isinstance(chunk, dict) and isinstance(chunk.get("text"), str):
                        parts.append(chunk["text"])
        if parts:
            return "\n".join(p for p in parts if p)

    if isinstance(obj, dict) and "choices" in obj:
        choices = obj["choices"]
        if isinstance(choices, list) and len(choices) > 0 and isinstance(choices[0], dict):
            msg = choices[0].get("message", {})
            if isinstance(msg, dict):
                return msg.get("content", "") or choices[0].get("text", "")
            return choices[0].get("text", "")

    # Already-extracted bodies. The conversational adapter captures from the workflow's own
    # trace, which stores the model's TEXT rather than the transport envelope the matrix
    # persists. Falling through to "" here reported every compose as a zero-word output and
    # made the script-evolution diff empty on that pipeline. A body that parsed as JSON but
    # carried no envelope (the writer's story briefs, the editor's verdict) is equally the
    # answer itself, so return it verbatim in both shapes.
    return resp_json if isinstance(resp_json, str) else ""


def extract_api_error(resp_json: str) -> str | None:
    """Detects a transient upstream failure (e.g. Azure OpenAI 'PermissionDenied' /
    rate-limit) captured verbatim instead of a real completion — verified live: the
    app's own client-level retry re-calls immediately and the next capture a moment
    later succeeds, but the failed attempt is still persisted as its own blob. Without
    this check it silently reads as a real '0-word' output, which is wrong and
    misleads word-count/verdict analysis downstream."""
    try:
        obj = json.loads(resp_json)
    except (json.JSONDecodeError, TypeError):
        return None
    if isinstance(obj, dict) and isinstance(obj.get("error"), dict):
        err = obj["error"]
        code = err.get("code", "UnknownError")
        message = err.get("message", "")
        return f"{code}: {message}" if message else code
    return None


def extract_usage(resp_json: str) -> dict:
    try:
        return json.loads(resp_json).get("usage", {})
    except (json.JSONDecodeError, TypeError):
        return {}


def try_parse_json(text: str) -> dict | None:
    """Review/Safety/Attribute stages return JSON as their responseText. Handles
    an occasional markdown code fence around it defensively."""
    if not text:
        return None
    text = text.strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*", "", text)
        text = re.sub(r"\s*```$", "", text)
    try:
        parsed = json.loads(text)
        return parsed if isinstance(parsed, dict) else None
    except json.JSONDecodeError:
        return None


def read_capture_file(path: Path) -> dict | None:
    try:
        c = json.loads(path.read_text("utf-8"))
    except (json.JSONDecodeError, OSError):
        return None
    resp_body = c.get("ResponseBody", "") or ""
    return {
        "stage": c.get("Stage"),
        "attempt": c.get("Attempt"),
        "model": c.get("Model"),
        "elapsedMs": c.get("ElapsedMs"),
        "timestamp": c.get("TimestampUtc"),
        "systemInstructions": c.get("SystemInstructions"),
        "renderedPrompt": c.get("RenderedPrompt"),
        "responseBody": c.get("ResponseBody"),
        "responseText": extract_response_text(resp_body),
        "usage": extract_usage(resp_body),
        "apiError": extract_api_error(resp_body),
        "sourceFile": path.name,
    }


# ─── which stage does what, per pipeline ────────────────────────────────────
# Two writers reach these captures. The structured matrix drafts in "Draft" and
# repairs in "Refine"; conversational-v1 composes in "compose2" and is briefed by
# "write". Naming them once here keeps every downstream count, diff and grouping
# from silently reporting zero on whichever pipeline it was not written for.

# Stages whose output IS the read, so word counts and script diffs chain through them.
SCRIPT_PRODUCING_STAGES = ("Draft", "Refine", "compose2")

# Stages that begin a fresh top-level attempt: a full restart rather than a repair.
ATTEMPT_OPENING_STAGES = ("Draft", "write")

# Stages that return a verdict on a finished read.
VERDICT_STAGES = ("Review", "edit")


def parse_capture_semantics(c: dict) -> None:
    """Adds stage-specific structured fields in place so an LLM doesn't have to
    re-parse responseText JSON for every card: wordCount for Draft/Refine,
    parsedVerdict for Review, parsedSafety for Safety, parsedAttribution for
    Attribute."""
    text = c.get("responseText") or ""
    stage = c.get("stage")
    if stage in SCRIPT_PRODUCING_STAGES:
        c["wordCount"] = len(text.split()) if (text and not c.get("apiError")) else None
    elif stage in VERDICT_STAGES:
        parsed = try_parse_json(text)
        if parsed:
            c["parsedVerdict"] = {
                "verdict": parsed.get("verdict"),
                "issues": parsed.get("issues", []) or [],
            }
    elif stage == "Safety":
        parsed = try_parse_json(text)
        if parsed:
            c["parsedSafety"] = {
                "safe": parsed.get("safe"),
                "violations": parsed.get("violations", []) or [],
            }
    elif stage == "Attribute":
        parsed = try_parse_json(text)
        if parsed:
            c["parsedAttribution"] = {"attributions": parsed.get("attributions", []) or []}


# ─── word-level diffing (script evolution across rounds) ───────────────────

def word_diff_stats(old: str, new: str) -> dict:
    old_words, new_words = old.split(), new.split()
    sm = difflib.SequenceMatcher(None, old_words, new_words)
    added = removed = 0
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag in ("replace", "delete"):
            removed += i2 - i1
        if tag in ("replace", "insert"):
            added += j2 - j1
    return {"wordsAdded": added, "wordsRemoved": removed, "similarity": round(sm.ratio(), 3)}


def word_diff_markup(old: str, new: str) -> str:
    """Git-style inline word diff: [-removed-] {+added+}, rest unchanged."""
    old_words, new_words = old.split(), new.split()
    sm = difflib.SequenceMatcher(None, old_words, new_words)
    out = []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            out.extend(new_words[j1:j2])
        elif tag == "replace":
            out.append("[-" + " ".join(old_words[i1:i2]) + "-]")
            out.append("{+" + " ".join(new_words[j1:j2]) + "+}")
        elif tag == "delete":
            out.append("[-" + " ".join(old_words[i1:i2]) + "-]")
        elif tag == "insert":
            out.append("{+" + " ".join(new_words[j1:j2]) + "+}")
    return " ".join(out)


def build_script_evolution(captures: list[dict]) -> list[dict]:
    """Word-level diff between every consecutive Draft/Refine output. A
    transition INTO a Draft (isNewAttempt=True) is a full restart of a new
    top-level generation attempt, not a refinement of the previous text —
    low similarity there is expected and not itself a defect signal; low
    similarity on a Refine transition (isNewAttempt=False) is the interesting
    case (a "fix" that reworked far more than the reviewer asked for)."""
    versions = [
        {"captureIndex": i, "stage": c["stage"], "attempt": c.get("attempt"),
         "text": c["responseText"], "wordCount": c.get("wordCount")}
        for i, c in enumerate(captures)
        if c["stage"] in SCRIPT_PRODUCING_STAGES and c.get("responseText")
    ]
    evolution = []
    for i in range(1, len(versions)):
        prev, cur = versions[i - 1], versions[i]
        stats = word_diff_stats(prev["text"], cur["text"])
        evolution.append({
            "fromCaptureIndex": prev["captureIndex"],
            "toCaptureIndex": cur["captureIndex"],
            "isNewAttempt": cur["stage"] in ATTEMPT_OPENING_STAGES,
            "wordsAdded": stats["wordsAdded"],
            "wordsRemoved": stats["wordsRemoved"],
            "similarity": stats["similarity"],
            "diffMarkup": word_diff_markup(prev["text"], cur["text"]),
        })
    return evolution


def build_post_llm_delta(job: dict, captures: list[dict]) -> dict | None:
    """Compares the job's final ScriptText against the last captured Draft/Refine
    output. These are usually identical (the last Refine IS the shipped script) —
    but when they differ, real content was added/removed by deterministic code
    AFTER the LLM loop ended, with no LLM call to explain it (e.g. a safety/fidelity
    trim applied when the retry budget is exhausted). That delta is invisible to
    both the timeline and the raw captures individually; this is the only place
    it surfaces. Returns None when there's nothing to compare (no captures) or the
    two texts match exactly."""
    final_script = job.get("ScriptText") or ""
    last, last_idx = None, None
    for idx, c in reversed(list(enumerate(captures))):
        if c["stage"] in SCRIPT_PRODUCING_STAGES and c.get("responseText"):
            last, last_idx = c, idx
            break
    if last is None or not final_script:
        return None
    if last["responseText"].strip() == final_script.strip():
        return None
    stats = word_diff_stats(last["responseText"], final_script)
    return {
        "lastCaptureIndex": last_idx,
        "lastCaptureStage": last["stage"],
        "wordsAdded": stats["wordsAdded"],
        "wordsRemoved": stats["wordsRemoved"],
        "similarity": stats["similarity"],
        "diffMarkup": word_diff_markup(last["responseText"], final_script),
    }


# ─── grouping into generation attempts / rounds ────────────────────────────

def group_generation_attempts(captures: list[dict]) -> list[dict]:
    """Groups Draft/Review/Refine captures into top-level generation attempts
    and pairs Review+Refine calls into rounds by call order.

    [ROBUSTNESS FIX / VERIFIED LIVE #1]: the capture payload's own `attempt` field
    is NOT a reliable top-level-attempt counter — verified on job
    375ca5ba87cd489a86f4cb7646c73ed6, which ran two full "Generation attempt
    N/4" cycles per LogsJson (attempt 1 completed, passed safety, then was
    discarded as content-limited and retried) while EVERY capture in both
    cycles carried `attempt: 1`. A new Draft call is therefore treated as the
    authoritative boundary between top-level attempts instead. The raw
    `attempt` field is still exposed on each capture for transparency, just
    not trusted for grouping.

    [ROBUSTNESS FIX / VERIFIED LIVE #2]: a capture with `apiError` set (a
    transient upstream failure like Azure OpenAI 'PermissionDenied' that the
    app's own client-level retry immediately recovered from) carries no real
    generation content and must NEVER start a new attempt or round — verified
    on a local run where a failed Draft call would otherwise have registered as
    a spurious empty "Attempt 1" before the real Attempt 1 even began. Error
    captures are attached to whichever attempt is currently open as a
    'kind': 'api-error' entry instead.

    [INFERENCE CAVEAT]: round numbers within an attempt are inferred purely
    from chronological call order (a Review starts a new round; the next
    Refine, if any, closes it) — cross-check the `timeline` key for anything
    that looks structurally unusual."""
    attempts: list[dict] = []
    current: dict | None = None
    cur_round: dict | None = None
    round_no = 0

    for idx, c in enumerate(captures):
        stage = c["stage"]
        if c.get("apiError"):
            if current is not None:
                current["rounds"].append({
                    "kind": "api-error", "captureIndex": idx, "stage": stage, "error": c["apiError"],
                })
            continue
        if stage in ATTEMPT_OPENING_STAGES:
            current = {"attemptNumber": len(attempts) + 1, "rounds": [], "finalWordCountThisAttempt": None}
            attempts.append(current)
            round_no = 0
            cur_round = None
            current["rounds"].append({"kind": "draft", "stage": stage, "captureIndex": idx, "wordCount": c.get("wordCount")})
        elif stage == "Review":
            if current is None:  # defensive: a Review before any Draft should not happen
                current = {"attemptNumber": len(attempts) + 1, "rounds": [], "finalWordCountThisAttempt": None}
                attempts.append(current)
                round_no = 0
            round_no += 1
            verdict_info = c.get("parsedVerdict") or {}
            cur_round = {
                "kind": "review-refine",
                "round": round_no,
                "reviewCaptureIndex": idx,
                "verdict": verdict_info.get("verdict"),
                "issueCount": len(verdict_info.get("issues") or []),
                "refineCaptureIndex": None,
                "wordCountAfter": None,
            }
            current["rounds"].append(cur_round)
        elif stage in SCRIPT_PRODUCING_STAGES and current is not None:
            if cur_round is not None and cur_round.get("refineCaptureIndex") is None:
                cur_round["refineCaptureIndex"] = idx
                cur_round["wordCountAfter"] = c.get("wordCount")
            else:
                current["rounds"].append({"kind": "refine-orphan", "stage": stage, "captureIndex": idx, "wordCount": c.get("wordCount")})

    for att in attempts:
        final_word_count = None
        for r in reversed(att["rounds"]):
            if r["kind"] == "review-refine" and r.get("wordCountAfter") is not None:
                final_word_count = r["wordCountAfter"]
                break
            if r["kind"] in ("draft", "refine-orphan"):
                final_word_count = r.get("wordCount")
                break
        att["finalWordCountThisAttempt"] = final_word_count

    return attempts


def build_finalization(captures: list[dict]) -> dict:
    fin: dict = {}
    for idx, c in enumerate(captures):
        if c["stage"] == "Safety" and c.get("parsedSafety"):
            fin["safety"] = {"captureIndex": idx, **c["parsedSafety"]}
        elif c["stage"] == "Attribute" and c.get("parsedAttribution"):
            fin["attribution"] = {"captureIndex": idx, **c["parsedAttribution"]}
    return fin


# ─── evidence (ground truth to cross-check script claims against) ─────────

def build_evidence(job: dict) -> dict:
    def safe_json(key: str):
        raw = job.get(key)
        if not raw:
            return None
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            return None

    all_incidents = safe_json("AllIncidentsJson") or []
    selected_incidents = safe_json("IncidentsJson") or []

    def strip_geometry(inc: dict) -> dict:
        return {k: v for k, v in inc.items() if k != "Shape"}

    return {
        "corridors": safe_json("CorridorsJson") or [],
        "allIncidents": [strip_geometry(i) for i in all_incidents],
        "selectedIncidentKeys": [i.get("Key") for i in selected_incidents] if selected_incidents else [],
        "flowRoads": safe_json("FlowRoadsJson") or [],
        "generatorInput": safe_json("GeneratorInputJson"),
    }


# ─── timeline ───────────────────────────────────────────────────────────────

def assign_timeline_icon(msg: str) -> str:
    ml = msg.lower()
    if "orchestrat" in ml or ml.startswith("start"):
        return "start"
    if "fetch" in ml:
        return "fetch"
    if "attempt" in ml and "generation" in ml:
        return "attempt"
    if "draft" in ml or (("generated" in ml) and "refine" not in ml):
        return "draft"
    if "refined" in ml:
        return "refine"
    if "review" in ml:
        return "review"
    if "feedback" in ml or "NEEDS_REVISION" in msg:
        return "critique"
    if "fail" in ml:
        return "fail"
    if "pass" in ml or "success" in ml:
        return "pass"
    if "audio" in ml or "synthesiz" in ml:
        return "audio"
    if "saved" in ml:
        return "save"
    if "ready" in ml or "awaiting" in ml:
        return "complete"
    return "info"


# ─── summary (the LLM entry point — read this first) ───────────────────────

def build_summary(job: dict, captures: list[dict], generation_attempts: list[dict],
                   finalization: dict, expected_capture_count: int,
                   warnings: list[str]) -> dict:
    total_tokens = sum((c.get("usage", {}).get("total_tokens") or 0) for c in captures)
    total_elapsed_ms = sum((c.get("elapsedMs") or 0) for c in captures)

    verdict_sequence = [
        r.get("verdict") for att in generation_attempts for r in att["rounds"]
        if r["kind"] == "review-refine"
    ]
    issues_all = [
        issue for c in captures if c["stage"] == "Review" and c.get("parsedVerdict")
        for issue in (c["parsedVerdict"].get("issues") or [])
    ]

    streak = job.get("FallbackStreak") or 0
    final_script = job.get("ScriptText") or ""

    return {
        "jobId": job.get("RowKey"),
        "market": job.get("Market"),
        "station": job.get("Station"),
        "status": job.get("Status"),
        "targetDurationSeconds": job.get("TargetDurationSeconds"),
        "finalWordCount": len(final_script.split()) if final_script else 0,
        "fallback": {
            "used": streak > 0,
            "streak": streak,
            "reason": job.get("FallbackReason"),
            "sourceJobId": job.get("FallbackSourceJobId"),
            "appliedAt": job.get("FallbackAppliedAt"),
        },
        "generationAttemptCount": len(generation_attempts),
        "reviewRoundVerdicts": verdict_sequence,
        "totalIssuesRaised": len(issues_all),
        "safety": finalization.get("safety"),
        "llmCalls": {
            "captured": len(captures),
            "expectedFromBlobListing": expected_capture_count,
            "coverageOk": len(captures) == expected_capture_count,
            "totalTokens": total_tokens,
            "totalElapsedMs": total_elapsed_ms,
        },
        "warnings": warnings,
    }


def collect_warnings(job: dict, logs: list[dict], captures: list[dict],
                      generation_attempts: list[dict], base_warnings: list[str]) -> list[str]:
    warnings = list(base_warnings)
    if not captures:
        warnings.append(
            "No LLM call captures found (Debug:CaptureLlmCalls may have been off, "
            "or this job is outside the rolling Debug:MaxCapturedJobs window)."
        )
    streak = job.get("FallbackStreak") or 0
    if streak > 1:
        warnings.append(f"Chronic fallback: FallbackStreak={streak}. See fallbackChain for history.")
    elif streak == 1:
        warnings.append("Fallback used once. If FallbackSourceJobId is set, IncidentsJson/CorridorsJson on the "
                         "job record are inherited from that source job, not this job's own fetch.")
    if len(generation_attempts) > 1:
        warnings.append(
            f"{len(generation_attempts)} top-level generation attempts ran; only attempt "
            f"{generation_attempts[-1]['attemptNumber']} shipped. Earlier attempt(s) were fully drafted "
            f"(and may have passed safety) then discarded and retried — see timeline for the reason "
            f"(e.g. 'content-limited') and generationAttempts for each attempt's own capture indices."
        )
    grounding_fail_count = sum(
        1 for log in logs if "retry budget exhausted" in (log.get("Message") or "")
    )
    if grounding_fail_count:
        warnings.append(
            f"Final grounding check failed {grounding_fail_count} time(s) — script shipped with "
            f"reviewer-flagged issues unresolved after the retry budget ran out."
        )
    api_errors = [c for c in captures if c.get("apiError")]
    if api_errors:
        by_code: dict[str, int] = {}
        for c in api_errors:
            by_code[c["apiError"]] = by_code.get(c["apiError"], 0) + 1
        detail = "; ".join(f"{v}x {k}" for k, v in by_code.items())
        warnings.append(
            f"{len(api_errors)} capture(s) hit a transient upstream API error ({detail}) — the app's "
            f"own client-level retry recovered by re-calling immediately; excluded from generationAttempts "
            f"round/attempt boundaries but still present in captures[] with apiError set."
        )
    malformed = [c for c in captures if c["stage"] == "Review" and not c.get("parsedVerdict") and not c.get("apiError")]
    if malformed:
        warnings.append(f"{len(malformed)} Review capture(s) had non-JSON responseText — verdict/issues unavailable.")
    return warnings


# ─── markdown rendering (stdout default + SUMMARY.md) ──────────────────────

def render_markdown_summary(export: dict) -> str:
    s = export["summary"]
    md = [f"# Job {s['jobId']} — diagnostic summary", ""]
    md.append(f"- **Market / Station:** {s['market']} / {s['station']}")
    md.append(f"- **Status:** {s['status']}")
    md.append(f"- **Target duration:** {s['targetDurationSeconds']}s — **final word count:** {s['finalWordCount']}")

    fb = s["fallback"]
    if fb["used"]:
        md.append(f"- **Fallback:** USED — streak {fb['streak']}, reason: {fb['reason']!r}, source job: {fb['sourceJobId']}")
    else:
        md.append("- **Fallback:** not used")

    md.append(f"- **Generation attempts:** {s['generationAttemptCount']}")
    if s["reviewRoundVerdicts"]:
        md.append(f"- **Review round verdicts:** {' -> '.join(v or '?' for v in s['reviewRoundVerdicts'])}")
    md.append(f"- **Total issues raised across rounds:** {s['totalIssuesRaised']}")
    if s.get("safety"):
        v = s["safety"].get("violations") or []
        md.append(f"- **Safety:** safe={s['safety'].get('safe')}, violations={len(v)}")
    lc = s["llmCalls"]
    coverage = "OK" if lc["coverageOk"] else "MISMATCH"
    md.append(
        f"- **LLM calls:** {lc['captured']} captured (expected {lc['expectedFromBlobListing']}, "
        f"coverage {coverage}), {lc['totalTokens']} tokens, {lc['totalElapsedMs']}ms LLM time"
    )

    if s["warnings"]:
        md.append("")
        md.append("## Warnings")
        for w in s["warnings"]:
            md.append(f"- \u26a0 {w}")

    if export["generationAttempts"]:
        md.append("")
        md.append("## Generation attempts")
        for att in export["generationAttempts"]:
            md.append(f"### Attempt {att['attemptNumber']} (final word count: {att['finalWordCountThisAttempt']})")
            for r in att["rounds"]:
                if r["kind"] == "draft":
                    # Name the stage that actually ran. Printing "Draft" for a conversational
                    # job describes a call that pipeline never makes.
                    md.append(f"- {r.get('stage', 'Draft')} -> {r['wordCount']} words (capture #{r['captureIndex']})")
                elif r["kind"] == "review-refine":
                    verdict = r.get("verdict") or "?"
                    line = f"- Round {r['round']}: Review={verdict} ({r['issueCount']} issue(s), capture #{r['reviewCaptureIndex']})"
                    if r.get("refineCaptureIndex") is not None:
                        line += f" -> Refine -> {r.get('wordCountAfter')} words (capture #{r['refineCaptureIndex']})"
                    else:
                        line += " -> no refine (budget exhausted)"
                    md.append(line)
                elif r["kind"] == "refine-orphan":
                    # On conversational this is not an orphan: the workflow's own deterministic
                    # checks reject and recompose without an editor call, so a compose with no
                    # preceding verdict is the normal shape rather than a missing review.
                    stage = r.get("stage", "Refine")
                    note = ("recomposed after its own checks" if stage == "compose2"
                            else "no preceding review in this attempt")
                    md.append(f"- {stage} ({note}) -> {r.get('wordCount')} words (capture #{r['captureIndex']})")
                elif r["kind"] == "api-error":
                    md.append(f"- \u26a0 API ERROR ({r['stage']}, capture #{r['captureIndex']}): {r['error']} \u2014 app retried automatically")

    if export["scriptEvolution"]:
        md.append("")
        md.append("## Script evolution (word-level diff between consecutive Draft/Refine versions)")
        for e in export["scriptEvolution"]:
            tag = " [NEW ATTEMPT RESTART]" if e.get("isNewAttempt") else ""
            md.append(
                f"- capture #{e['fromCaptureIndex']} -> #{e['toCaptureIndex']}{tag}: "
                f"+{e['wordsAdded']}/-{e['wordsRemoved']} words, similarity {e['similarity']}"
            )

    if export.get("postLlmDelta"):
        d = export["postLlmDelta"]
        md.append("")
        md.append("## \u26a0 Post-LLM delta (final script changed with NO LLM call to explain it)")
        md.append(
            f"- Last captured output (capture #{d['lastCaptureIndex']}, {d['lastCaptureStage']}) vs "
            f"final job.ScriptText: +{d['wordsAdded']}/-{d['wordsRemoved']} words, similarity {d['similarity']}"
        )
        md.append(f"- Diff: `{d['diffMarkup']}`")

    if export.get("fallbackChain"):
        md.append("")
        md.append(f"## Fallback chain ({len(export['fallbackChain'])} hop(s), most recent first)")
        for hop in export["fallbackChain"]:
            md.append(
                f"- {hop['scheduledForUtc']} {hop['jobId']} {hop['status']} "
                f"streak={hop['fallbackStreak']} reason={hop['fallbackReason']}"
            )

    md.append("")
    files = export["_files"]
    md.append(f"Full data: `{files['json']}` (JSON, read `_guide` first) / `{files['html']}` (HTML, open in a browser)")
    return "\n".join(md) + "\n"


def render_capture_table(captures: list[dict]) -> str:
    lines = [f"{'IDX':<4} {'STAGE':<10} {'ATTEMPT':<8} {'ELAPSED':<9} DETAIL"]
    for i, c in enumerate(captures):
        detail = ""
        if c.get("apiError"):
            detail = f"API ERROR: {c['apiError']}"
        elif c["stage"] in ("Draft", "Refine"):
            detail = f"{c.get('wordCount', '?')} words"
        elif c["stage"] == "Review" and c.get("parsedVerdict"):
            v = c["parsedVerdict"]
            detail = f"{v.get('verdict')} ({len(v.get('issues') or [])} issue(s))"
        elif c["stage"] == "Safety" and c.get("parsedSafety"):
            detail = f"safe={c['parsedSafety'].get('safe')}"
        elif c["stage"] == "Attribute" and c.get("parsedAttribution"):
            detail = f"{len(c['parsedAttribution'].get('attributions') or [])} sentence(s) attributed"
        lines.append(
            f"{i:<4} {c.get('stage', '?'):<10} {str(c.get('attempt', '?')):<8} "
            f"{str(c.get('elapsedMs', '?')) + 'ms':<9} {detail}"
        )
    return "\n".join(lines)


def render_capture_raw(c: dict, idx: int) -> str:
    error_line = f"--- API ERROR ---\n{c['apiError']}\n\n" if c.get("apiError") else ""
    return (
        f"=== Capture #{idx}: {c.get('stage')} (attempt {c.get('attempt')}) "
        f"\u2014 {c.get('timestamp')}, {c.get('elapsedMs')}ms, model={c.get('model')} ===\n\n"
        f"{error_line}"
        f"--- SYSTEM INSTRUCTIONS ---\n{c.get('systemInstructions')}\n\n"
        f"--- RENDERED PROMPT ---\n{c.get('renderedPrompt')}\n\n"
        f"--- RESPONSE TEXT ---\n{c.get('responseText')}\n"
    )


# ─── comparison (baseline vs new run — the prompt-iteration loop) ─────────

def record_local_run(job_id: str, out_dir: Path) -> None:
    LOCAL_RUNS_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    runs = []
    if LOCAL_RUNS_MANIFEST.exists():
        try:
            runs = json.loads(LOCAL_RUNS_MANIFEST.read_text("utf-8"))
        except json.JSONDecodeError:
            runs = []
    runs.append({
        "jobId": job_id, "dir": str(out_dir),
        "at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    })
    LOCAL_RUNS_MANIFEST.write_text(json.dumps(runs[-50:], indent=2), "utf-8")


def resolve_compare_target(spec: str) -> str:
    """Called BEFORE this run is recorded into the manifest, so 'previous'
    correctly means the run before this one, not this one."""
    if spec not in ("previous", "last"):
        return spec
    if not LOCAL_RUNS_MANIFEST.exists():
        die("--compare previous: no local runs recorded yet. Run once without --compare first.")
    runs = json.loads(LOCAL_RUNS_MANIFEST.read_text("utf-8"))
    if not runs:
        die("--compare previous: no local runs recorded yet. Run once without --compare first.")
    return runs[-1]["dir"]


def load_export_for_compare(path_or_dir: str) -> dict:
    p = Path(path_or_dir)
    if p.is_dir():
        p = p / "prompt-export.json"
    if not p.exists():
        die(f"--compare target not found: {p}")
    return json.loads(p.read_text("utf-8"))


def _warning_category(w: str) -> str:
    """Strips capture indices/counts so the same warning CATEGORY (e.g. postLlmDelta,
    grounding-exhausted) with different numbers doesn't show as both a new AND a
    resolved warning between two runs — only a genuinely different category should."""
    return re.sub(r"#?\d+(\.\d+)?", "#", w)


def build_comparison(baseline: dict, new: dict) -> dict:
    b, n = baseline["summary"], new["summary"]
    b_script = baseline.get("job", {}).get("ScriptText") or ""
    n_script = new.get("job", {}).get("ScriptText") or ""
    stats = word_diff_stats(b_script, n_script)

    b_warnings = b.get("warnings") or []
    n_warnings = n.get("warnings") or []
    b_categories = {_warning_category(w) for w in b_warnings}
    n_categories = {_warning_category(w) for w in n_warnings}

    return {
        "baseline": {
            "jobId": b.get("jobId"), "wordCount": b.get("finalWordCount"),
            "attempts": b.get("generationAttemptCount"), "issues": b.get("totalIssuesRaised"),
            "verdicts": b.get("reviewRoundVerdicts"),
        },
        "new": {
            "jobId": n.get("jobId"), "wordCount": n.get("finalWordCount"),
            "attempts": n.get("generationAttemptCount"), "issues": n.get("totalIssuesRaised"),
            "verdicts": n.get("reviewRoundVerdicts"),
        },
        "deltas": {
            "wordCount": (n.get("finalWordCount") or 0) - (b.get("finalWordCount") or 0),
            "attempts": (n.get("generationAttemptCount") or 0) - (b.get("generationAttemptCount") or 0),
            "issuesRaised": (n.get("totalIssuesRaised") or 0) - (b.get("totalIssuesRaised") or 0),
            "totalTokens": (n.get("llmCalls", {}).get("totalTokens") or 0) - (b.get("llmCalls", {}).get("totalTokens") or 0),
        },
        "warnings": {
            "new": [w for w in n_warnings if _warning_category(w) not in b_categories],
            "resolved": [w for w in b_warnings if _warning_category(w) not in n_categories],
        },
        "scriptDiff": {**stats, "diffMarkup": word_diff_markup(b_script, n_script)},
    }


def render_comparison_markdown(cmp: dict) -> str:
    md = ["# Comparison: baseline vs new run", ""]
    b, n, d = cmp["baseline"], cmp["new"], cmp["deltas"]
    md.append(f"- Baseline: `{b['jobId']}` \u2014 {b['wordCount']} words, {b['attempts']} attempt(s), {b['issues']} issue(s)")
    md.append(f"- New:      `{n['jobId']}` \u2014 {n['wordCount']} words, {n['attempts']} attempt(s), {n['issues']} issue(s)")
    md.append("")
    md.append(f"- Word count delta: {d['wordCount']:+d}")
    md.append(f"- Attempts delta: {d['attempts']:+d}")
    md.append(f"- Issues-raised delta: {d['issuesRaised']:+d}")
    md.append(f"- Token-cost delta: {d['totalTokens']:+d}")
    if cmp["warnings"]["new"]:
        md.append("")
        md.append("## New warnings (possible regressions)")
        for w in cmp["warnings"]["new"]:
            md.append(f"- \u26a0 {w}")
    if cmp["warnings"]["resolved"]:
        md.append("")
        md.append("## Resolved warnings")
        for w in cmp["warnings"]["resolved"]:
            md.append(f"- \u2713 {w}")
    md.append("")
    md.append(f"## Script diff (baseline \u2192 new) \u2014 similarity {cmp['scriptDiff']['similarity']}, "
               f"+{cmp['scriptDiff']['wordsAdded']}/-{cmp['scriptDiff']['wordsRemoved']} words")
    md.append(f"`{cmp['scriptDiff']['diffMarkup']}`")
    return "\n".join(md) + "\n"


# ─── HTML viewer template ───────────────────────────────────────────────────

VIEWER_HTML = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Job %JOB_ID% — Prompt Timeline</title>
<style>
:root{--bg:#0d1117;--bg2:#161b22;--bg3:#21262d;--border:#30363d;--text:#e6edf3;--text-dim:#8b949e;--text-bright:#f0f6fc;--accent:#58a6ff;--green:#3fb950;--orange:#d29922;--red:#f85149;--purple:#bc8cff;--cyan:#39dada;--pink:#f778ba}
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Arial,sans-serif;background:var(--bg);color:var(--text);line-height:1.6}
.header{background:linear-gradient(135deg,#0d1117,#161b22);border-bottom:1px solid var(--border);padding:20px 32px;position:sticky;top:0;z-index:100}
.header h1{font-size:18px;font-weight:600;color:var(--text-bright);margin-bottom:4px}
.header .meta{color:var(--text-dim);font-size:13px;display:flex;gap:14px;flex-wrap:wrap;align-items:center}
.badge{display:inline-block;padding:1px 8px;border-radius:12px;font-size:10px;font-weight:600;text-transform:uppercase}
.badge-warning{background:#3a2f1a;color:var(--orange)}.badge-success{background:#1a3a2a;color:var(--green)}.badge-fail{background:#3a1a1a;color:var(--red)}
.container{max-width:1200px;margin:0 auto;padding:20px 32px}
h2{font-size:15px;font-weight:600;color:var(--text-bright);margin:28px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--border);display:flex;align-items:center;gap:8px}
h2 .count{font-size:11px;font-weight:500;color:var(--text-dim);background:var(--bg3);padding:0 8px;border-radius:8px}
.src-link{font-size:12px;color:var(--text-dim);margin-bottom:12px}.src-link a{color:var(--accent)}
/* summary panel */
.summary-panel{background:var(--bg2);border:1px solid var(--border);border-radius:8px;padding:16px;margin-bottom:8px}
.summary-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:10px}
.sbox{background:var(--bg3);border-radius:6px;padding:10px 12px}
.sbox .lbl{font-size:10px;font-weight:600;text-transform:uppercase;color:var(--text-dim);margin-bottom:3px}
.sbox .val{color:var(--text);font-size:13px}
.warn-list{margin-top:12px;padding-top:12px;border-top:1px solid var(--border)}
.warn-item{color:var(--orange);font-size:12px;padding:3px 0;display:flex;gap:6px}
/* pipeline nav strip */
.pipeline-nav{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:20px;padding:10px;background:var(--bg2);border:1px solid var(--border);border-radius:8px}
.pnav-chip{font-size:10px;font-weight:700;text-transform:uppercase;padding:3px 8px;border-radius:10px;cursor:pointer;border:1px solid transparent;white-space:nowrap}
.pnav-chip:hover{border-color:var(--accent)}
.pnav-chip .dot{display:inline-block;width:6px;height:6px;border-radius:50%;margin-left:4px}
/* timeline */
.timeline{position:relative;padding-left:26px}
.timeline::before{content:'';position:absolute;left:11px;top:4px;bottom:4px;width:2px;background:var(--border)}
.tl-item{position:relative;margin-bottom:1px;padding:5px 10px;border-radius:4px;display:flex;align-items:flex-start;gap:8px;font-size:12px;line-height:1.5}
.tl-item:hover{background:var(--bg2)}
.tl-item .icon{position:absolute;left:-20px;top:7px;width:18px;height:18px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:9px;font-weight:700;border:2px solid var(--bg)}
.i-start{background:var(--accent);color:var(--bg)}.i-fetch{background:var(--cyan);color:var(--bg)}.i-info{background:var(--bg3);color:var(--text-dim)}
.i-attempt{background:var(--orange);color:var(--bg)}.i-draft{background:var(--purple);color:var(--bg)}
.i-review{background:var(--accent);color:var(--bg)}.i-critique{background:var(--orange);color:var(--bg)}
.i-refine{background:var(--pink);color:var(--bg)}.i-fail{background:var(--red);color:#fff}
.i-pass{background:var(--green);color:var(--bg)}.i-audio{background:var(--cyan);color:var(--bg)}
.i-save{background:var(--accent);color:var(--bg)}.i-complete{background:var(--green);color:var(--bg)}
.tl-item .t{color:var(--text-dim);font-family:'SF Mono',Consolas,monospace;font-size:11px;min-width:90px;flex-shrink:0}
.tl-item .m{color:var(--text)}
/* evidence */
.evidence-toggle{cursor:pointer;user-select:none}
.evidence-body{display:none}
.evidence-body.open{display:block}
table.ev-table{width:100%;border-collapse:collapse;font-size:11px;margin-bottom:12px}
table.ev-table th{text-align:left;color:var(--text-dim);font-weight:600;text-transform:uppercase;font-size:9px;padding:6px 8px;border-bottom:1px solid var(--border)}
table.ev-table td{padding:5px 8px;border-bottom:1px solid #1c2129;vertical-align:top}
table.ev-table tr:hover td{background:var(--bg2)}
.sel-yes{color:var(--green)}.sel-no{color:var(--text-dim)}
/* evolution */
.evo-item{background:var(--bg2);border:1px solid var(--border);border-radius:6px;padding:10px 12px;margin-bottom:8px;font-size:12px}
.evo-item .evo-hd{display:flex;gap:12px;color:var(--text-dim);margin-bottom:6px;font-size:11px}
.evo-item .diff{font-family:'SF Mono',Consolas,monospace;font-size:11px;line-height:1.6;white-space:pre-wrap;word-break:break-word}
.diff del{background:#3a1a1a;color:var(--red);text-decoration:line-through;border-radius:2px}
.diff ins{background:#1a3a2a;color:var(--green);text-decoration:none;border-radius:2px}
/* generation attempts */
.attempt-block{background:var(--bg2);border:1px solid var(--border);border-radius:8px;padding:12px 14px;margin-bottom:10px}
.attempt-block.discarded{opacity:0.7;border-style:dashed}
.attempt-hd{font-size:12px;font-weight:700;color:var(--text-bright);margin-bottom:8px;display:flex;gap:10px;align-items:center}
.attempt-hd .discarded-tag{font-size:10px;font-weight:700;text-transform:uppercase;color:var(--orange);background:#3a2f1a;padding:1px 8px;border-radius:8px}
.round-row{display:flex;align-items:center;gap:8px;padding:4px 0;font-size:12px;flex-wrap:wrap}
.round-row .rlabel{color:var(--text-dim);min-width:70px;font-size:11px}
.round-row a{color:var(--accent);text-decoration:none;cursor:pointer}
.round-row a:hover{text-decoration:underline}
.round-row .rverdict{font-size:10px;font-weight:700;padding:1px 7px;border-radius:8px}
/* search/filter */
.filter-row{display:flex;gap:8px;margin-bottom:12px;align-items:center;flex-wrap:wrap}
.filter-row input[type=text]{flex:1;min-width:200px;background:var(--bg3);border:1px solid var(--border);color:var(--text);padding:6px 10px;border-radius:6px;font-size:12px}
.filter-row label{font-size:11px;color:var(--text-dim);display:flex;align-items:center;gap:4px;cursor:pointer}
/* cards */
.card{background:var(--bg2);border:1px solid var(--border);border-radius:8px;margin-bottom:16px;overflow:hidden}
.card-hd{display:flex;align-items:center;gap:10px;padding:10px 14px;background:var(--bg3);border-bottom:1px solid var(--border);cursor:pointer;user-select:none}
.card-hd:hover{background:#2a2f38}
.card-hd .stage{display:inline-block;padding:2px 8px;border-radius:10px;font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.4px}
.s-Draft{background:#3a1a5a;color:#d4a0ff}.s-Review{background:#1a3a5a;color:var(--accent)}
.s-Refine{background:#3a2a1a;color:var(--pink)}.s-Safety{background:#1a3a2a;color:var(--green)}
.s-Attribute{background:#2a1a3a;color:var(--purple)}
.card-hd .idx{font-size:10px;color:var(--text-dim);font-family:'SF Mono',monospace}
.card-hd .verdict{font-size:10px;font-weight:700;padding:1px 7px;border-radius:8px}
.verdict-approved{background:#1a3a2a;color:var(--green)}.verdict-needs_revision{background:#3a1a1a;color:var(--red)}
.card-hd .info{font-size:12px;color:var(--text-dim);display:flex;gap:12px;flex-wrap:wrap;flex:1}
.card-hd .toggle{color:var(--text-dim);font-size:14px}
.card-bd{display:none}.card-bd.open{display:block}
.sec{padding:10px 14px;border-bottom:1px solid var(--border)}
.sec:last-child{border-bottom:none}
.sec h3{font-size:10px;font-weight:600;text-transform:uppercase;letter-spacing:.7px;color:var(--text-dim);margin-bottom:6px}
.issues-list{margin:0 0 8px 0;padding-left:18px;font-size:12px}
.issues-list li{margin-bottom:4px;color:var(--text)}
.code{background:#010409;border:1px solid var(--border);border-radius:5px;padding:10px;overflow-x:auto;font-family:'SF Mono','Cascadia Code',Consolas,monospace;font-size:11px;line-height:1.5;color:var(--text);white-space:pre-wrap;word-break:break-word;max-height:500px;overflow-y:auto}
.code .ln{display:inline-block;width:28px;color:var(--text-dim);text-align:right;margin-right:10px;user-select:none}
.code-tall{max-height:700px}.code-full{max-height:none}
.ctx-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:10px;margin-bottom:20px}
.ctx-box{background:var(--bg2);border:1px solid var(--border);border-radius:6px;padding:12px;font-size:13px}
.ctx-box .lbl{font-size:10px;font-weight:600;text-transform:uppercase;color:var(--text-dim);margin-bottom:3px}
.ctx-box .val{color:var(--text)}
.ctrls{display:flex;gap:6px;margin-bottom:12px}
.ctrls button{background:var(--bg3);border:1px solid var(--border);color:var(--text);padding:4px 12px;border-radius:5px;font-size:11px;font-weight:500;cursor:pointer}
.ctrls button:hover{background:var(--border)}
@media(max-width:768px){.container{padding:12px 16px}.header{padding:16px}.tl-item{flex-direction:column;gap:1px}.tl-item .t{min-width:auto}.code{font-size:10px}}
</style>
</head>
<body>
<div class="header">
<h1 id="page-title">Loading...</h1>
<div class="meta" id="page-meta"></div>
</div>
<div class="container">
<p class="src-link">Data: <a href="prompt-export.json">prompt-export.json</a> (LLM-readable) &middot; <a href="SUMMARY.md">SUMMARY.md</a> (digest)%COMPARE_LINK%</p>

<div class="summary-panel" id="summary-panel"></div>

<div id="pipeline-nav" class="pipeline-nav"></div>

<div id="content">
<h2>Job Context</h2>
<div class="ctx-grid" id="context-grid"></div>

<h2>Pipeline Timeline <span class="count" id="tl-count"></span></h2>
<div class="timeline" id="timeline"></div>

<h2 style="margin-top:32px">Generation Attempts <span class="count" id="attempts-count"></span></h2>
<div id="gen-attempts"></div>

<h2 style="margin-top:32px">Script Evolution <span class="count" id="evo-count"></span></h2>
<div id="evolution"></div>

<h2 style="margin-top:32px" class="evidence-toggle" onclick="toggleEvidence()">Evidence Data (raw traffic feed) <span class="count" id="ev-count"></span> <span id="ev-caret" style="font-size:11px">&#x25B6;</span></h2>
<div class="evidence-body" id="evidence-body"></div>

<h2 style="margin-top:32px">LLM Agent Calls <span class="count" id="card-count"></span></h2>
<div class="filter-row">
<input type="text" id="search-box" placeholder="Search prompts/responses...">
<label><input type="checkbox" class="stage-filter" value="Draft" checked>Draft</label>
<label><input type="checkbox" class="stage-filter" value="Review" checked>Review</label>
<label><input type="checkbox" class="stage-filter" value="Refine" checked>Refine</label>
<label><input type="checkbox" class="stage-filter" value="Safety" checked>Safety</label>
<label><input type="checkbox" class="stage-filter" value="Attribute" checked>Attribute</label>
</div>
<div class="ctrls"><button onclick="expandAll()">+ Expand All</button><button onclick="collapseAll()">- Collapse All</button></div>
<div id="cards"></div>
</div>
</div>
<script id="prompt-data" type="application/json">
%JSON_DATA%
</script>
<script>
function esc(s){if(!s)return '';return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;')}
function addLn(s){if(!s)return '';return s.split('\n').map(function(l,i){return '<span class="ln">'+String(i+1).padStart(3)+'</span>'+esc(l)}).join('\n')}
function iconHtml(k){var m={start:'\u25CF',fetch:'\u2191',info:'i',attempt:'A',draft:'D',review:'R',critique:'\u2717',refine:'\u2192',fail:'\u2717',pass:'\u2713',audio:'\u266A',save:'\u2193',complete:'\u25CF'};return '<div class="icon i-'+k+'">'+(m[k]||'\u2022')+'</div>'}
function toggleCard(h){var b=h.nextElementSibling,t=h.querySelector('.toggle');b.classList.toggle('open');t.textContent=b.classList.contains('open')?'\u25BC':'\u25B6'}
function expandAll(){document.querySelectorAll('.card-bd').forEach(function(b){b.classList.add('open');b.previousElementSibling.querySelector('.toggle').textContent='\u25BC'})}
function collapseAll(){document.querySelectorAll('.card-bd').forEach(function(b){b.classList.remove('open');b.previousElementSibling.querySelector('.toggle').textContent='\u25B6'})}
function toggleEvidence(){var b=document.getElementById('evidence-body'),c=document.getElementById('ev-caret');b.classList.toggle('open');c.innerHTML=b.classList.contains('open')?'\u25BC':'\u25B6'}
function scrollToCapture(i){var el=document.getElementById('capture-'+i);if(!el)return;var body=el.querySelector('.card-bd'),hd=el.querySelector('.card-hd');if(body&&!body.classList.contains('open')){body.classList.add('open');hd.querySelector('.toggle').textContent='\u25BC'}el.scrollIntoView({behavior:'smooth',block:'start'})}
function renderDiffMarkup(s){
  var esc_ = esc(s);
  esc_ = esc_.replace(/\[-(.*?)-\]/g, function(m, g){ return '<del>'+g+'</del>'; });
  esc_ = esc_.replace(/\{\+(.*?)\+\}/g, function(m, g){ return '<ins>'+g+'</ins>'; });
  return esc_;
}
function fmtUsage(u){if(!u)return '';return 'Prompt: '+(u.prompt_tokens||u.input_tokens||'?')+' \u00b7 Completion: '+(u.completion_tokens||u.output_tokens||'?')+' \u00b7 Total: '+(u.total_tokens||'?')}

function renderSummary(data){
  var s=data.summary||{}, files=data._files||{};
  document.getElementById('page-title').textContent='Job '+(s.jobId||data.jobId||'')+' \u2014 Prompt Timeline';
  var m='';
  if(s.market)m+='<span>'+esc(s.market)+'</span>';
  if(s.station&&s.station!==s.market)m+='<span>'+esc(s.station)+'</span>';
  if(s.status)m+='<span><span class="badge badge-'+(s.status==='WaitingApproval'?'warning':'success')+'">'+esc(s.status)+'</span></span>';
  document.getElementById('page-meta').innerHTML=m;

  var p=document.getElementById('summary-panel');
  var g='<div class="summary-grid">';
  g+=box('Target / Final', (s.targetDurationSeconds||'?')+'s target, '+(s.finalWordCount||0)+' words final');
  g+=box('Fallback', s.fallback&&s.fallback.used?('USED (streak '+s.fallback.streak+')'):'not used');
  g+=box('Generation Attempts', s.generationAttemptCount);
  g+=box('Review Verdicts', (s.reviewRoundVerdicts||[]).map(function(v){return v||'?'}).join(' \u2192 ')||'\u2014');
  g+=box('Issues Raised', s.totalIssuesRaised);
  if(s.safety) g+=box('Safety', 'safe='+s.safety.safe+', '+((s.safety.violations||[]).length)+' violation(s)');
  var lc=s.llmCalls||{};
  g+=box('LLM Calls', lc.captured+' captured, '+lc.totalTokens+' tokens, '+lc.totalElapsedMs+'ms');
  g+='</div>';
  if(s.warnings&&s.warnings.length){
    g+='<div class="warn-list">';
    s.warnings.forEach(function(w){g+='<div class="warn-item">\u26a0 '+esc(w)+'</div>'});
    g+='</div>';
  }
  p.innerHTML=g;
  function box(l,v){return '<div class="sbox"><div class="lbl">'+l+'</div><div class="val">'+esc(String(v))+'</div></div>'}
}

function renderPipelineNav(captures){
  var sc={Draft:'#d4a0ff',Review:'#58a6ff',Refine:'#f778ba',Safety:'#3fb950',Attribute:'#bc8cff'};
  var html=captures.map(function(c,i){
    var color=sc[c.stage]||'#8b949e';
    var dot='';
    if(c.stage==='Review'&&c.parsedVerdict){
      dot='<span class="dot" style="background:'+(c.parsedVerdict.verdict==='APPROVED'?'#3fb950':'#f85149')+'"></span>';
    }
    return '<span class="pnav-chip" style="color:'+color+';border-color:'+color+'33" onclick="scrollToCapture('+i+')">#'+i+' '+c.stage+dot+'</span>';
  }).join('');
  document.getElementById('pipeline-nav').innerHTML=html;
}

function renderContext(job, captures){
  var cg=document.getElementById('context-grid');
  var items=[
    {l:'Market / Station',v:job.Market||'\u2014'},
    {l:'Status',v:job.Status||'\u2014'},
    {l:'Target Duration',v:job.TargetDurationSeconds?job.TargetDurationSeconds+'s':'\u2014'},
    {l:'FallbackStreak',v:(job.FallbackStreak===0?'0 (none)':String(job.FallbackStreak||'\u2014'))},
    {l:'Audio',v:job.AudioUrl?'<a href="'+esc(job.AudioUrl)+'" style="color:var(--accent)">file</a>':'\u2014'},
    {l:'LLM Calls',v:captures.length+' captured'},
    {l:'Scheduled',v:job.ScheduledForUtc||'\u2014'},
    {l:'Created',v:job.CreatedAt||'\u2014'},
  ];
  cg.innerHTML=items.map(function(i){return '<div class="ctx-box"><div class="lbl">'+i.l+'</div><div class="val">'+i.v+'</div></div>'}).join('');
}

function renderTimeline(tl){
  document.getElementById('tl-count').textContent=tl.length+' events';
  document.getElementById('timeline').innerHTML=tl.map(function(t){
    return '<div class="tl-item">'+iconHtml(t.ic||'info')+'<div class="t">'+(t.time||'')+'</div><div class="m">'+esc(t.msg||'')+'</div></div>';
  }).join('');
}

function renderGenerationAttempts(attempts){
  document.getElementById('attempts-count').textContent=attempts.length+(attempts.length>1?' (only the last shipped)':'');
  document.getElementById('gen-attempts').innerHTML=attempts.map(function(att, i){
    var isLast=i===attempts.length-1;
    var discarded=!isLast;
    var rowsHtml=att.rounds.map(function(r){
      if(r.kind==='draft'){
        return '<div class="round-row"><span class="rlabel">'+(r.stage||'Draft')+'</span><a onclick="scrollToCapture('+r.captureIndex+')">#'+r.captureIndex+'</a><span>'+r.wordCount+' words</span></div>';
      }
      if(r.kind==='review-refine'){
        var vClass=r.verdict==='APPROVED'?'verdict-approved':'verdict-needs_revision';
        var refineHtml=r.refineCaptureIndex!==null?(' \u2192 Refine <a onclick="scrollToCapture('+r.refineCaptureIndex+')">#'+r.refineCaptureIndex+'</a> <span>'+r.wordCountAfter+' words</span>'):' \u2192 <span style="color:var(--text-dim)">no refine (budget exhausted)</span>';
      return '<div class="round-row"><span class="rlabel">Round '+r.round+'</span>Review <a onclick="scrollToCapture('+r.reviewCaptureIndex+')">#'+r.reviewCaptureIndex+'</a> <span class="rverdict '+vClass+'">'+esc(r.verdict||'?')+'</span> ('+r.issueCount+' issue(s))'+refineHtml+'</div>';
      }
      if(r.kind==='api-error'){
        return '<div class="round-row" style="color:var(--red)"><span class="rlabel">\u26a0 API error</span>('+esc(r.stage)+') <a onclick="scrollToCapture('+r.captureIndex+')">#'+r.captureIndex+'</a><span>'+esc(r.error)+' \u2014 app retried automatically</span></div>';
      }
      var st = r.stage || 'Refine';
      var note = st === 'compose2' ? 'recomposed after its own checks' : 'no preceding review this attempt';
      return '<div class="round-row"><span class="rlabel">'+st+'</span><a onclick="scrollToCapture('+r.captureIndex+')">#'+r.captureIndex+'</a><span>'+r.wordCount+' words ('+note+')</span></div>';
    }).join('');
    return '<div class="attempt-block'+(discarded?' discarded':'')+'"><div class="attempt-hd">Attempt '+att.attemptNumber+' \u2014 final word count '+att.finalWordCountThisAttempt+
      (discarded?'<span class="discarded-tag">discarded, retried</span>':'<span class="discarded-tag" style="color:var(--green);background:#1a3a2a">shipped</span>')+
      '</div>'+rowsHtml+'</div>';
  }).join('') || '<p style="color:var(--text-dim);font-size:12px">No generation attempts (no Draft/Review/Refine captures).</p>';
}

function renderEvolution(evo, postDelta){
  var extra = 0;
  document.getElementById('evolution').innerHTML=evo.map(function(e){
    var boundary = e.isNewAttempt ? '<span style="color:var(--orange);font-weight:700">\u21bb NEW ATTEMPT RESTART</span>' : '';
    return '<div class="evo-item"'+(e.isNewAttempt?' style="border-color:var(--orange)"':'')+'><div class="evo-hd"><span>capture #'+e.fromCaptureIndex+' \u2192 #'+e.toCaptureIndex+'</span>'+boundary+
      '<span>+'+e.wordsAdded+' / -'+e.wordsRemoved+' words</span><span>similarity '+e.similarity+'</span></div>'+
      '<div class="diff">'+renderDiffMarkup(e.diffMarkup)+'</div></div>';
  }).join('') || '<p style="color:var(--text-dim);font-size:12px">No Draft/Refine transitions to diff.</p>';
  if(postDelta){
    extra = 1;
    document.getElementById('evolution').innerHTML += '<div class="evo-item" style="border-color:var(--red)"><div class="evo-hd"><span style="color:var(--red);font-weight:700">\u26a0 POST-LLM DELTA</span>'+
      '<span>capture #'+postDelta.lastCaptureIndex+' ('+postDelta.lastCaptureStage+') \u2192 final job.ScriptText</span>'+
      '<span>+'+postDelta.wordsAdded+' / -'+postDelta.wordsRemoved+' words</span><span>similarity '+postDelta.similarity+'</span></div>'+
      '<div style="font-size:11px;color:var(--text-dim);margin-bottom:6px">Content changed with NO LLM call to explain it \u2014 deterministic post-processing ran after the LLM loop ended.</div>'+
      '<div class="diff">'+renderDiffMarkup(postDelta.diffMarkup)+'</div></div>';
  }
  document.getElementById('evo-count').textContent=evo.length+' transition(s)'+(extra?' + 1 post-LLM delta':'');
}

function renderEvidence(ev){
  document.getElementById('ev-count').textContent=(ev.corridors||[]).length+' corridors, '+(ev.allIncidents||[]).length+' incidents, '+(ev.flowRoads||[]).length+' flow roads';
  var html='';
  html+='<h3 style="font-size:11px;color:var(--text-dim);text-transform:uppercase;margin-bottom:6px">Corridors</h3>';
  html+='<table class="ev-table"><tr><th>Road</th><th>Type</th><th>Description</th><th>Severity</th><th>Selected</th></tr>';
  (ev.corridors||[]).forEach(function(c){
    html+='<tr><td>'+esc(c.RoadName)+' '+esc(c.Direction||'')+'</td><td>'+esc(c.PrimaryType)+'</td><td>'+esc(c.Description)+'</td><td>'+c.Severity+'</td><td class="'+(c.IsSelected?'sel-yes':'sel-no')+'">'+(c.IsSelected?'yes':'no')+'</td></tr>';
  });
  html+='</table>';
  html+='<h3 style="font-size:11px;color:var(--text-dim);text-transform:uppercase;margin:12px 0 6px">All Incidents ('+(ev.allIncidents||[]).length+')</h3>';
  html+='<table class="ev-table"><tr><th>Key</th><th>Headline</th><th>Detail</th><th>Selected</th></tr>';
  var selectedSet={};(ev.selectedIncidentKeys||[]).forEach(function(k){selectedSet[k]=true});
  (ev.allIncidents||[]).forEach(function(i){
    var sel=selectedSet[i.Key];
    html+='<tr><td style="font-family:monospace;font-size:9px">'+esc(String(i.Key).slice(-6))+'</td><td>'+esc(i.Headline)+'</td><td>'+esc(i.Detail)+'</td><td class="'+(sel?'sel-yes':'sel-no')+'">'+(sel?'yes':'no')+'</td></tr>';
  });
  html+='</table>';
  if(ev.flowRoads&&ev.flowRoads.length){
    html+='<h3 style="font-size:11px;color:var(--text-dim);text-transform:uppercase;margin:12px 0 6px">Flow Roads</h3>';
    html+='<table class="ev-table"><tr><th>Road</th><th>Jam Factor</th><th>Score</th><th>Selected</th></tr>';
    ev.flowRoads.forEach(function(f){
      html+='<tr><td>'+esc(f.RoadName)+'</td><td>'+f.JamFactor+'</td><td>'+f.Score+'</td><td class="'+(f.IsSelected?'sel-yes':'sel-no')+'">'+(f.IsSelected?'yes':'no')+'</td></tr>';
    });
    html+='</table>';
  }
  document.getElementById('evidence-body').innerHTML=html;
}

function extractRespText(c){
  if(c.apiError)return '[API ERROR] '+c.apiError+'\n\n(the app\'s own client-level retry recovered by re-calling immediately \u2014 see the next capture)';
  if(c.responseText)return c.responseText;
  try{var o=JSON.parse(c.responseBody);if(o.output&&o.output.length){var m=o.output[0];if(m.content&&m.content.length)return m.content.map(function(x){return x.text||''}).join('\n')}if(o.choices&&o.choices.length)return(o.choices[0].message&&o.choices[0].message.content)||o.choices[0].text||''}catch(e){}
  return c.responseBody?'(see raw body)':'(no response)';
}

var ALL_CAPTURES = [];

function renderCards(captures){
  document.getElementById('card-count').textContent=captures.length+' calls';
  var ce=document.getElementById('cards');
  if(!captures.length){ce.innerHTML='<p style="color:var(--text-dim);padding:20px 0;">No LLM call captures for this job.</p>';return}
  var sc={Draft:'s-Draft',Review:'s-Review',Refine:'s-Refine',Safety:'s-Safety',Attribute:'s-Attribute'},em={Draft:'\uD83D\uDCDD',Review:'\uD83D\uDD0D',Refine:'\uD83D\uDEE0',Safety:'\uD83D\uDEE1',Attribute:'\uD83C\uDFF7'};

  ce.innerHTML=captures.map(function(c,i){
    var e=em[c.stage]||'\uD83E\uDD16',st=sc[c.stage]||'',ts=c.timestamp?(c.timestamp.split('T')[1]||'').substring(0,12):'',us=fmtUsage(c.usage),op=i===0,rt=extractRespText(c);
    // Every stage is now a DIFFERENT Foundry agent (arn-draft, arn-review, arn-refine-sentence,
    // arn-safety, arn-director), each with its own server-side instructions. The viewer used to
    // print them once on the Draft card and label the rest 'same as Draft card', which was true
    // only while every stage hit the raw model with PromptLibrary.SystemInstructions.
    var prevSame=i>0&&captures[i-1].systemInstructions===c.systemInstructions;
    var ss=c.systemInstructions?('<div class="sec"><h3>System Instructions <span style="font-weight:400;text-transform:none;color:var(--text-dim)">('+(prevSame?'identical to the previous card':(c.stage||'stage')+' agent, '+String(c.systemInstructions.length)+' chars')+')</span></h3><div class="code code-full"><pre style="margin:0;font-family:inherit;font-size:inherit;white-space:inherit;word-break:inherit">'+addLn(c.systemInstructions)+'</pre></div></div>'):'<div class="sec"><h3>System Instructions <span style="font-weight:400;text-transform:none;color:var(--text-dim)">(none captured — server-side agent definition)</span></h3></div>';

    var verdictBadge='';
    var issuesHtml='';
    if(c.stage==='Review'&&c.parsedVerdict){
      var vClass=c.parsedVerdict.verdict==='APPROVED'?'verdict-approved':'verdict-needs_revision';
      verdictBadge='<span class="verdict '+vClass+'">'+esc(c.parsedVerdict.verdict||'?')+'</span>';
      if(c.parsedVerdict.issues&&c.parsedVerdict.issues.length){
        issuesHtml='<div class="sec"><h3>Parsed Issues ('+c.parsedVerdict.issues.length+')</h3><ul class="issues-list">'+c.parsedVerdict.issues.map(function(it){return '<li>'+esc(it)+'</li>'}).join('')+'</ul></div>';
      }
    }
    if(c.stage==='Safety'&&c.parsedSafety){
      verdictBadge='<span class="verdict '+(c.parsedSafety.safe?'verdict-approved':'verdict-needs_revision')+'">'+(c.parsedSafety.safe?'SAFE':'UNSAFE')+'</span>';
    }
    if((c.stage==='Draft'||c.stage==='Refine')&&c.wordCount!==undefined&&c.wordCount!==null){
      verdictBadge='<span class="idx">'+c.wordCount+' words</span>';
    }
    if(c.apiError){
      verdictBadge='<span class="verdict verdict-needs_revision">API ERROR</span>';
    }

    return '<div class="card" id="capture-'+i+'" data-stage="'+c.stage+'"><div class="card-hd" onclick="toggleCard(this)">'+
      '<span class="idx">#'+i+'</span>'+
      '<span class="stage '+st+'">'+e+' '+c.stage+'</span>'+
      verdictBadge+
      '<div class="info"><span>Attempt '+c.attempt+'</span><span>\u23F1 '+(c.elapsedMs||'')+'ms</span><span>'+esc(ts)+'</span><span style="font-size:10px">'+esc(c.model||'')+'</span>'+(us?'<span style="font-size:10px">'+us+'</span>':'')+'</div>'+
      '<span class="toggle">'+(op?'\u25BC':'\u25B6')+'</span></div>'+
      '<div class="card-bd'+(op?' open':'')+'">'+ss+issuesHtml+
      '<div class="sec"><h3>Stage-Specific Rendered Prompt</h3><div class="code code-tall"><pre style="margin:0;font-family:inherit;font-size:inherit;white-space:inherit;word-break:inherit">'+addLn(c.renderedPrompt)+'</pre></div></div>'+
      '<div class="sec"><h3>Model Response'+(c.stage==='Review'||c.stage==='Safety'||c.stage==='Attribute'?' (raw JSON)':'')+'</h3><div class="code code-tall"><pre style="margin:0;font-family:inherit;font-size:inherit;white-space:inherit;word-break:inherit">'+addLn(rt)+'</pre></div></div>'+
      '</div></div>';
  }).join('');
}

function applyFilters(){
  var q=document.getElementById('search-box').value.toLowerCase();
  var activeStages={};
  document.querySelectorAll('.stage-filter').forEach(function(cb){if(cb.checked)activeStages[cb.value]=true});
  document.querySelectorAll('.card').forEach(function(card){
    var stage=card.getAttribute('data-stage');
    var idx=parseInt(card.id.replace('capture-',''),10);
    var c=ALL_CAPTURES[idx];
    var text=((c.renderedPrompt||'')+' '+(c.responseText||'')+' '+(c.systemInstructions||'')).toLowerCase();
    var stageOk=activeStages[stage];
    var textOk=!q||text.indexOf(q)!==-1;
    card.style.display=(stageOk&&textOk)?'':'none';
  });
}

function render(data){
  ALL_CAPTURES=data.captures||[];
  renderSummary(data);
  renderPipelineNav(data.captures||[]);
  renderContext(data.job||{}, data.captures||[]);
  renderTimeline(data.timeline||[]);
  renderGenerationAttempts(data.generationAttempts||[]);
  renderEvolution(data.scriptEvolution||[], data.postLlmDelta);
  renderEvidence(data.evidence||{});
  renderCards(data.captures||[]);
  document.getElementById('search-box').addEventListener('input', applyFilters);
  document.querySelectorAll('.stage-filter').forEach(function(cb){cb.addEventListener('change', applyFilters)});
}

var de=document.getElementById('prompt-data');
if(de){
  try{render(JSON.parse(de.textContent))}
  catch(e){document.getElementById('content').innerHTML='<div style="background:#3a1a1a;border:1px solid var(--red);border-radius:8px;padding:24px;text-align:center;margin:40px 0"><h3 style="color:var(--red);font-size:16px;margin-bottom:8px">Error rendering</h3><p style="color:var(--text-dim);font-size:13px">'+esc(e.message)+'</p></div>'}
}
</script>
</body>
</html>"""


# ─── main ───────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        description=__doc__.split("\n\n")[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("job", nargs="?", default=None,
                         help="Job id or operator-console job URL. Omit with --local to trigger a fresh one.")
    parser.add_argument("--local", action="store_true",
                         help="Target the local dev stack (Azurite + func host on :7071) instead of a deployed environment")
    parser.add_argument("--env", choices=list(ENV_STORAGE), default=None,
                         help="[deployed only] Restrict to one environment. Default: auto-probe dev, then test, then prod.")
    parser.add_argument("--out", default=None, help="Output directory (default ./job-diagnostics/<jobId>/, or .../local/<jobId>/ with --local)")
    parser.add_argument("--refresh", action="store_true", help="Force re-download of LLM captures")
    parser.add_argument("--quiet", action="store_true", help="Write files only, print nothing to stdout")
    parser.add_argument("--no-html", action="store_true", help="Skip writing prompt-viewer.html")
    parser.add_argument("--no-fallback-chain", action="store_true", help="Skip walking the fallback chain")
    parser.add_argument("--format", choices=["md", "json"], default="md",
                         help="stdout format: md (default, human+LLM digest) or json (full export, for jq)")
    parser.add_argument("--show", type=int, metavar="INDEX",
                         help="Print one capture's full raw system/prompt/response to stdout and exit")
    parser.add_argument("--list-captures", action="store_true",
                         help="Print a compact capture table to stdout and exit")
    parser.add_argument("--compare", default=None, metavar="DIR_OR_previous",
                         help="Diff this run against a prior prompt-export.json — a path, a job dir, or 'previous' (last recorded --local run)")
    parser.add_argument("--build-fixture", metavar="LABEL", default=None,
                         help="Build a regression-corpus fixture (test/corpus/regressions/<LABEL>.json) from "
                              "this job's exact original data, for replaying under a prompt/code change")
    parser.add_argument("--reason", default=None,
                         help="Why this job was flagged (used with --build-fixture; default generic)")
    parser.add_argument("--replay-fixture", action="store_true",
                         help="With --build-fixture: also replay it LIVE through the CURRENT prompts/code "
                              "(dotnet build+test, real Azure OpenAI calls) and print a before/after diff")


    local_group = parser.add_argument_group("--local trigger options (ignored without --local, or when a job id is given)")
    local_group.add_argument("--market", default="sydney",
                              help=f"Market preset to trigger (default sydney). One of: {', '.join(MARKET_PRESETS)}, or use --lat/--lng for a custom one")
    local_group.add_argument("--duration", type=int, default=30, help="Target bulletin duration in seconds (default 30)")
    local_group.add_argument("--station", default=None, help="Override station/location name")
    local_group.add_argument("--voice-id", default=None, help="Override ElevenLabs voice id")
    local_group.add_argument("--voice-name", default=None, help="Override voice name")
    local_group.add_argument("--lat", type=float, default=None, help="Override coverage latitude")
    local_group.add_argument("--lng", type=float, default=None, help="Override coverage longitude")
    local_group.add_argument("--radius", type=float, default=None, help="Override coverage radius (km)")
    local_group.add_argument("--script-only", action="store_true", help="Skip TTS synthesis (script generation only, faster)")
    local_group.add_argument("--trigger-timeout", type=int, default=180, help="Seconds to wait for the triggered job to finish (default 180)")
    local_group.add_argument("--prompts-dir", default=None,
                              help="Use an experimental prompts/ variant via ARN_PROMPTS_DIR (forces a host restart; directory must contain all 8 *.md files)")
    local_group.add_argument("--restart-host", action="store_true",
                              help="Force a host restart even if capture is already enabled (use after hand-editing prompts/*.md)")
    local_group.add_argument("--no-restart", action="store_true",
                              help="Never auto-restart the host, even if Debug:CaptureLlmCalls needs enabling")
    args = parser.parse_args()

    check_az_available()

    if args.local:
        if args.env:
            die("--env is not compatible with --local (local mode always targets the Azurite emulator).")
        conn_args = conn_args_for_local()
        env_label = "local"

        if args.job:
            # Inspect an existing local job — no trigger, no restart.
            job_id = extract_job_id(args.job)
            print(f"Inspecting existing local job {job_id}...", file=sys.stderr)
            job = fetch_job(conn_args, job_id)
            if not job:
                die(f"Job {job_id} not found in the local Azurite radioJobs table.")
        else:
            host_up = check_local_host()
            capture_already_on = False
            if LOCAL_SETTINGS_PATH.exists():
                try:
                    vals = json.loads(LOCAL_SETTINGS_PATH.read_text("utf-8")).get("Values", {})
                    capture_already_on = str(vals.get("Debug__CaptureLlmCalls", "")).lower() == "true"
                except json.JSONDecodeError:
                    pass

            needs_restart = args.restart_host or bool(args.prompts_dir) or not host_up or not capture_already_on
            if needs_restart and args.no_restart:
                reasons = []
                if not host_up:
                    reasons.append("host not running")
                if not capture_already_on:
                    reasons.append("Debug:CaptureLlmCalls not enabled")
                if args.restart_host:
                    reasons.append("--restart-host requested")
                if args.prompts_dir:
                    reasons.append("--prompts-dir requested")
                die(f"A host restart is required ({', '.join(reasons)}) but --no-restart was set.")

            if needs_restart:
                if not capture_already_on:
                    ensure_capture_setting()
                    print("Enabled Debug:CaptureLlmCalls in local.settings.json.", file=sys.stderr)
                restart_local_host(args.prompts_dir)

            print(f"Triggering local bulletin: market={args.market} duration={args.duration}s"
                  + (f" prompts-dir={args.prompts_dir}" if args.prompts_dir else "") + "...", file=sys.stderr)
            job_id = trigger_local_bulletin(
                args.market, args.duration, args.station, args.voice_id, args.voice_name,
                args.lat, args.lng, args.radius, args.script_only,
            )
            print(f"Triggered job {job_id}, waiting for pipeline...", file=sys.stderr)
            poll_local_job(job_id, args.trigger_timeout)
            job = fetch_job(conn_args, job_id)
            if not job:
                die(f"Job {job_id} completed via the API but was not found in the local radioJobs table.")

        out_dir = Path(args.out) if args.out else Path("job-diagnostics") / "local" / job_id
    else:
        if not args.job:
            die("A job id or URL is required unless --local is used to trigger a fresh one.")
        job_id = extract_job_id(args.job)
        out_dir = Path(args.out) if args.out else Path("job-diagnostics") / job_id
        check_subscription()
        print(f"Fetching job {job_id}...", file=sys.stderr)
        env_label, job = find_job(job_id, args.env)
        conn_args = conn_args_for_account(ENV_STORAGE[env_label])

    logs = parse_logs(job)
    compare_target = resolve_compare_target(args.compare) if args.compare else None

    files, expected_count, coverage_warnings = ensure_captures(conn_args, job_id, out_dir, args.refresh)
    if files:
        print(f"Using {len(files)} captured LLM call(s) in {out_dir / 'llm_calls'}", file=sys.stderr)

    captures: list[dict] = []
    parse_warnings: list[str] = []
    for f in files:
        c = read_capture_file(f)
        if c is None:
            parse_warnings.append(f"Could not parse capture file: {f.name}")
            continue
        parse_capture_semantics(c)
        captures.append(c)

    generation_attempts = group_generation_attempts(captures)
    finalization = build_finalization(captures)
    script_evolution = build_script_evolution(captures)
    post_llm_delta = build_post_llm_delta(job, captures)
    evidence = build_evidence(job)

    timeline = []
    for log in logs:
        msg = log.get("Message", "")
        timeline.append({"time": log.get("At", ""), "msg": msg, "ic": assign_timeline_icon(msg)})

    fallback_chain = None
    if job.get("FallbackSourceJobId") and not args.no_fallback_chain:
        print("Walking fallback chain...", file=sys.stderr)
        fallback_chain = walk_fallback_chain(conn_args, job_id)

    base_warnings = coverage_warnings + parse_warnings
    if post_llm_delta is not None:
        base_warnings.append(
            f"Final ScriptText differs from the last captured LLM output (capture "
            f"#{post_llm_delta['lastCaptureIndex']}): +{post_llm_delta['wordsAdded']}/"
            f"-{post_llm_delta['wordsRemoved']} words with NO LLM call to explain it — "
            f"deterministic post-processing (e.g. a safety/fidelity trim) ran after the "
            f"LLM loop ended. See postLlmDelta for the exact diff."
        )
    all_warnings = collect_warnings(job, logs, captures, generation_attempts, base_warnings)
    summary = build_summary(job, captures, generation_attempts, finalization, expected_count, all_warnings)

    out_dir.mkdir(parents=True, exist_ok=True)
    files_meta = {
        "json": "prompt-export.json",
        "html": "prompt-viewer.html",
        "summary": "SUMMARY.md",
    }

    export = {
        "_guide": {
            "readFirst": "summary",
            "structure": {
                "summary": "Computed diagnosis: status, fallback state, verdict sequence, token/time cost, warnings. Read this first.",
                "job": "Raw job record: final script (ScriptText), market/station, timing, fallback pointers.",
                "timeline": "Chronological LogsJson entries (the human-narrated pipeline log), each tagged with an icon hint. Messages may be truncated at the source (observed ~850 chars) — prefer parsedVerdict.issues on the matching capture for the full text.",
                "generationAttempts": "Draft/Review/Refine captures grouped into top-level generation attempts, each split into rounds (Review+Refine pairs). Attempt boundaries are detected from Draft-call occurrence, NOT the capture's own `attempt` field (verified unreliable — see code comment in group_generation_attempts). More than one attempt means earlier attempt(s) were fully generated then discarded and retried (see warnings + timeline for why); only the LAST attempt's script/audio shipped. Round numbers within an attempt are inferred from call order. captureIndex fields point into captures[].",
                "scriptEvolution": "Word-level diff + similarity ratio between each consecutive Draft/Refine script version. Low similarity on a round that should have been a small fix, or a fix that appears then vanishes next attempt, is the signature of the 'fix discarded on restart' defect class.",
                "postLlmDelta": "Present only when job.ScriptText differs from the LAST captured Draft/Refine output. Non-null means content changed with NO LLM call to explain it — investigate the C# code path for the retry-budget-exhausted / finalization case, not the prompts.",
                "finalization": "Safety check and sentence-to-corridor attribution run on the FINAL approved script (captureIndex points into captures[]).",
                "evidence": "Raw traffic data (corridors/incidents/flowRoads, geometry stripped) fed into generation. Cross-check script claims in job.ScriptText against this to catch invented/misattributed facts.",
                "captures": "Every raw LLM call in order: systemInstructions + renderedPrompt + responseText/responseBody + usage. Indexed 0..N-1; referenced by captureIndex elsewhere. responseText is pre-extracted; parsedVerdict/parsedSafety/parsedAttribution/wordCount are pre-parsed where applicable. apiError is non-null when this specific call hit a transient upstream failure (e.g. Azure OpenAI PermissionDenied/rate-limit) that the app's own client retry immediately recovered from — such captures carry no real content and are excluded from generationAttempts round/attempt boundaries.",
                "fallbackChain": "Present only when job.FallbackSourceJobId is set: the chain of prior jobs this job's evidence/script was inherited from, most recent first.",
            },
        },
        "jobId": job_id,
        "environment": env_label,
        "exportedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "_files": files_meta,
        "summary": summary,
        "job": {
            "RowKey": job.get("RowKey"),
            "Status": job.get("Status"),
            "Market": job.get("Market"),
            "Station": job.get("Station"),
            "TargetDurationSeconds": job.get("TargetDurationSeconds"),
            "ScriptText": job.get("ScriptText"),
            "FallbackStreak": job.get("FallbackStreak"),
            "FallbackReason": job.get("FallbackReason"),
            "FallbackSourceJobId": job.get("FallbackSourceJobId"),
            "FallbackAppliedAt": job.get("FallbackAppliedAt"),
            "AudioUrl": job.get("AudioUrl"),
            "ScheduledForUtc": job.get("ScheduledForUtc"),
            "CreatedAt": job.get("CreatedAt"),
            "LastUpdatedAt": job.get("LastUpdatedAt"),
        },
        "timeline": timeline,
        "generationAttempts": generation_attempts,
        "scriptEvolution": script_evolution,
        "postLlmDelta": post_llm_delta,
        "finalization": finalization,
        "evidence": evidence,
        "captures": captures,
    }
    if fallback_chain is not None:
        export["fallbackChain"] = fallback_chain

    export_json = json.dumps(export, indent=2, default=str)
    (out_dir / "prompt-export.json").write_text(export_json, "utf-8")

    md_summary = render_markdown_summary(export)
    (out_dir / "SUMMARY.md").write_text(md_summary, "utf-8")

    cmp_md = None
    if compare_target:
        print(f"Comparing against {compare_target}...", file=sys.stderr)
        baseline_export = load_export_for_compare(compare_target)
        comparison = build_comparison(baseline_export, export)
        cmp_md = render_comparison_markdown(comparison)
        (out_dir / "COMPARE.md").write_text(cmp_md, "utf-8")
        (out_dir / "compare.json").write_text(json.dumps(comparison, indent=2, default=str), "utf-8")

    if not args.no_html:
        html_data = json.dumps(export, default=str)
        compare_link = ' &middot; <a href="COMPARE.md">COMPARE.md</a> (vs baseline)' if cmp_md else ""
        html = (VIEWER_HTML.replace("%JOB_ID%", job_id)
                .replace("%JSON_DATA%", html_data)
                .replace("%COMPARE_LINK%", compare_link))
        (out_dir / "prompt-viewer.html").write_text(html, "utf-8")

    if args.local:
        record_local_run(job_id, out_dir)

    fixture_md = None
    if args.build_fixture:
        reason = args.reason or f"flagged via export-prompts.py --build-fixture on {job_id}"
        fixture_result = build_and_replay_fixture(job, job_id, args.build_fixture, reason, args.replay_fixture, args.prompts_dir)
        if fixture_result:
            fixture_md = render_fixture_replay_markdown(fixture_result)
            (out_dir / "FIXTURE-REPLAY.md").write_text(fixture_md, "utf-8")

    wrote = "prompt-export.json,SUMMARY.md" + ("" if args.no_html else ",prompt-viewer.html") + ("" if not cmp_md else ",COMPARE.md,compare.json") + ("" if not fixture_md else ",FIXTURE-REPLAY.md")
    print(f"Wrote {out_dir}/{{{wrote}}}", file=sys.stderr)

    if args.show is not None:
        if not (0 <= args.show < len(captures)):
            die(f"--show {args.show} out of range (0..{len(captures) - 1})")
        print(render_capture_raw(captures[args.show], args.show))
        return

    if args.list_captures:
        print(render_capture_table(captures))
        return

    if args.quiet:
        return

    if args.format == "json":
        print(export_json)
    else:
        print(md_summary)

    if cmp_md:
        print()
        print(cmp_md)

    if fixture_md:
        print()
        print(fixture_md)


if __name__ == "__main__":
    main()
