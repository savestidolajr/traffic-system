#!/usr/bin/env python3
"""pipeline_burst.py — end-to-end generation load: fire N concurrent broadcast
requests and measure queue-to-terminal latency against SLO 3 (P99 <= 240s).

This is the load that matters operationally: the schedule poller enqueuing
every market's bulletin in the same minute. Each job exercises the full
pipeline (HERE fetch -> LLM draft/review -> ElevenLabs synthesis -> audio
post-processing -> WaitingApproval), so EVERY JOB COSTS REAL VENDOR SPEND
(HERE tile/flow calls + Azure OpenAI tokens + ElevenLabs characters).
Estimate before raising --jobs; see docs/operations/load-testing.md.

Auth: same constraint as api-read.js — the ARM-audience bearer works on DEV
only. Test/prod runs are blocked on an ARN automation identity.

Usage:
  pipeline_burst.py --base-url https://ibp-aue-dev-fna-app01.azurewebsites.net \
      [--jobs 11] [--duration 25] [--timeout 360] [--out results.json]
"""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request

# One representative coverage per market (mirrors the seeded MarketSchedules).
MARKETS = [
    ("Sydney", -33.8688, 151.2093, 25),
    ("Melbourne", -37.8136, 144.9631, 25),
    ("Brisbane", -27.4698, 153.0251, 25),
    ("Gold Coast", -28.0167, 153.4000, 20),
    ("Perth", -31.9505, 115.8605, 25),
    ("Adelaide", -34.9285, 138.6007, 20),
    ("Darwin", -12.4634, 130.8456, 15),
    ("Mildura", -34.2080, 142.1246, 15),
    ("Port Augusta", -32.4936, 137.7743, 15),
    ("Port Lincoln", -34.7217, 135.8558, 15),
    ("Queenstown", -42.0805, 145.5522, 15),
]

TERMINAL = {"WAITING_APPROVAL", "FAILED", "FALLBACK_USED", "PUBLISHED"}


def arm_token() -> str:
    return subprocess.run(
        ["az", "account", "get-access-token", "--resource",
         "https://management.azure.com", "--query", "accessToken", "-o", "tsv"],
        capture_output=True, text=True, check=True).stdout.strip()


def call(base: str, token: str, method: str, path: str, body: dict | None = None,
         timeout: int = 60, tries: int = 4) -> tuple[int, dict | str]:
    """HTTP with retry/backoff on TRANSPORT errors.

    A bare urlopen here used to raise URLError (local `Connection refused`) straight out of a
    worker thread, which killed the whole ThreadPoolExecutor batch mid-run and lost every other
    market's result. That happened on the 16 Jul dev run AND the 26 Jul test run, and both times
    the outcomes had to be reconstructed from the job records by hand. A transient blip on the
    client side must never destroy a burst that the platform is completing correctly.
    HTTPError is a real answer from the server, so it is returned, not retried.
    """
    req = urllib.request.Request(
        f"{base}/api{path}", method=method,
        data=json.dumps(body).encode() if body else None,
        headers={"Authorization": f"Bearer {token}",
                 "Content-Type": "application/json"})
    last = "unknown transport error"
    for attempt in range(tries):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                raw = resp.read()
                return resp.status, (json.loads(raw) if raw else {})
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode()[:300]
        except (urllib.error.URLError, TimeoutError, OSError, json.JSONDecodeError) as e:
            last = f"{type(e).__name__}: {e}"
            if attempt < tries - 1:
                time.sleep(2 ** attempt)
    return 0, f"transport failed after {tries} attempts — {last}"


def resolve_voice_profile(base: str, token: str, requested: str | None) -> str:
    if requested:
        return requested
    status, result = call(base, token, "GET", "/voices/profiles")
    items = result if isinstance(result, list) else (result or {}).get("Items") or (result or {}).get("Profiles") or (result or {}).get("VoiceProfiles") or (result or {}).get("profiles") or []
    for profile in items:
        voice = profile.get("Id") or profile.get("id") or profile.get("VoiceProfileId")
        if voice:
            return voice
    raise RuntimeError("no voice profiles available; pass --voice-profile-id <id>")

def run_job(base: str, token: str, market: tuple, duration: int, timeout: int, voice_profile_id: str) -> dict:
    name, lat, lng, radius = market
    t0 = time.monotonic()
    try:
        status, resp = call(base, token, "POST", "/broadcast", {
            "Location": name, "VoiceProfileId": voice_profile_id, "TargetDurationSeconds": duration,
            "Latitude": lat, "Longitude": lng, "RadiusKm": radius})
        if status != 202 or not isinstance(resp, dict):
            return {"market": name, "reachedTerminal": False, "fresh": False,
                    "error": f"queue {status}: {resp}"}
        job_id = resp.get("jobId") or resp.get("JobId")

        final = "TIMEOUT"
        while time.monotonic() - t0 < timeout:
            s, j = call(base, token, "GET", f"/jobs/{job_id}")
            if s == 200 and isinstance(j, dict) and j.get("Status") in TERMINAL:
                final = j["Status"]
                break
            time.sleep(5)
        elapsed = time.monotonic() - t0
        return {"market": name, "jobId": job_id,
                # SLO-3 is a statement about GENERATION LATENCY, so any job that reached a
                # terminal state is measurable — including FALLBACK_USED, which on a quiet
                # market is the pipeline behaving correctly, not a failure.
                "reachedTerminal": final in TERMINAL,
                "fresh": final == "WAITING_APPROVAL",
                "finalStatus": final, "elapsedSeconds": round(elapsed, 1)}
    except Exception as e:  # noqa: BLE001 - one market must never abort the batch
        return {"market": name, "reachedTerminal": False, "fresh": False,
                "error": f"{type(e).__name__}: {e}",
                "elapsedSeconds": round(time.monotonic() - t0, 1)}


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--base-url", required=True)
    p.add_argument("--jobs", type=int, default=len(MARKETS),
                   help="concurrent jobs (default: one per market, 11)")
    p.add_argument("--duration", type=int, default=25,
                   help="bulletin slot seconds (default 25)")
    p.add_argument("--timeout", type=int, default=360,
                   help="per-job wall timeout seconds (SLO ceiling 240 + margin)")
    p.add_argument("--out", default=None, help="write JSON report here")
    p.add_argument("--voice-profile-id", default=None, help="voice profile used for every synthetic market")
    args = p.parse_args()

    token = arm_token()
    try:
        voice_profile_id = resolve_voice_profile(args.base_url, token, args.voice_profile_id)
    except RuntimeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    targets = (MARKETS * ((args.jobs // len(MARKETS)) + 1))[: args.jobs]
    print(f"Firing {len(targets)} concurrent broadcasts at {args.base_url} ...")

    started = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(targets)) as ex:
        results = list(ex.map(
            lambda m: run_job(args.base_url, token, m, args.duration, args.timeout, voice_profile_id),
            targets))


    # SLO-3 measures how long generation takes, NOT whether the market had enough content to
    # avoid a standby. Measuring latency only over WAITING_APPROVAL jobs made a quiet-market
    # burst report sloGenerationP99Under240s=false even when every job finished far inside the
    # ceiling (26 Jul test run: 10/10 terminal, max 128s, would have reported false). Latency is
    # now measured over everything that reached a terminal state; content outcome is reported
    # alongside it instead of being folded into the same number.
    terminal = [r for r in results if r.get("reachedTerminal")]
    fresh = [r for r in terminal if r.get("fresh")]
    stuck = [r for r in results if not r.get("reachedTerminal")]
    lat = sorted(r["elapsedSeconds"] for r in terminal)
    counts: dict[str, int] = {}
    for r in results:
        counts[r.get("finalStatus") or "NEVER_QUEUED"] = counts.get(r.get("finalStatus") or "NEVER_QUEUED", 0) + 1
    report = {
        "startedUtc": started,
        "baseUrl": args.base_url,
        "requested": len(targets),
        "reachedTerminal": len(terminal),
        "freshBulletins": len(fresh),
        "terminalStatusCounts": counts,
        "neverReachedTerminal": stuck,
        "latencySeconds": {
            "p50": round(statistics.median(lat), 1) if lat else None,
            "p95": round(lat[max(0, int(len(lat) * 0.95) - 1)], 1) if lat else None,
            "max": lat[-1] if lat else None,
        },
        "sloGenerationP99Under240s":
            (lat[-1] <= 240) if lat and len(lat) == len(targets) else False,
        "results": results,
    }
    text = json.dumps(report, indent=2)
    print(text)
    if args.out:
        with open(args.out, "w") as f:
            f.write(text)
    # Non-zero only when a job never reached a terminal state (a real harness/pipeline problem).
    # A standby-covered slot is a legitimate outcome and must not fail the run.
    return 0 if not stuck else 1


if __name__ == "__main__":
    sys.exit(main())
