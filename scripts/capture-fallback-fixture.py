#!/usr/bin/env python3
"""Capture one replay fixture from a deployed DEV job.

Why this exists
---------------
The fallback corpus began as an ad-hoc investigation dump. It captured only jobs that
fell back, so its ``devAgreement`` number could never detect a replay that was too harsh;
it also made repeating a capture depend on someone's shell history. This command is the
repeatable, read-only capture path. It records the job's terminal polarity, the narrated
per-attempt outcome in ``LogsJson``, the persisted writer input when available, and the
traffic/config data needed by the local replay harness.

Station configuration note
--------------------------
Historical job records do not persist the station filler library or the resolved bookend
pair. When capturing one of those jobs, this tool therefore reads today's DEV
``/api/config/export`` and resolves the job's market against the export's filler row and
schedule assignment. ``StationConfigSource`` records this approximation as
``live-station-config``; a future job carrying its own station snapshot remains
``job-snapshot``. This is intentionally not presented as historical configuration.

Usage
-----
    python3 scripts/capture-fallback-fixture.py <job-id>
    python3 scripts/capture-fallback-fixture.py <job-id> --snapshot-file /path/to/radioJobs.json

The default path obtains a management token with ``az account get-access-token`` and reads
``/api/jobs?limit=500`` followed by ``/api/jobs/<id>`` from DEV. It also reads the
read-only ``/api/config/export`` endpoint to fill missing historical station configuration.
It never calls a mutating endpoint. ``--snapshot-file`` is an optional local copy of a
persisted ``radioJobs`` row; when supplied, its GeneratorInputJson is used verbatim because
the public detail DTO does not currently expose that storage column. If no stored snapshot
is available, the tool builds the same safe, replayable shape from the detail DTO's
Traffic/Weather/Fuel fields.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import subprocess
import sys
import urllib.error
import urllib.request
from typing import Any

DEFAULT_API = "https://ibp-aue-dev-fna-app01.azurewebsites.net/api"
ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT_ROOT = ROOT / "test" / "corpus" / "fallback-replay"
CONFIG_EXPORT_URL = f"{DEFAULT_API}/config/export"

# The historical job market is normally the schedule MarketId. These two explicit aliases
# are retained because the panel contains the human-facing schedule name "Gold Coast Drive
# Traffic" and the legacy "TEST STATION" label, while DEV stores those rows as "goldcoast"
# and "test station". Do not replace this with slug/punctuation matching: a near match could
# silently borrow another station's filler or bookend pair (2026-08-23).
MARKET_ALIASES = {
    "gold coast drive traffic": "goldcoast",
    "test station": "test station",
}

# Keep token substitution aligned with Domain.Services.MarketNames and fail closed for an
# unknown market. The export has schedule ids, not the domain's display-name table.
MARKET_DISPLAY_NAMES = {
    "sydney": "Sydney",
    "melbourne": "Melbourne",
    "brisbane": "Brisbane",
    "perth": "Perth",
    "adelaide": "Adelaide",
    "darwin": "Darwin",
    "gold coast": "Gold Coast",
    "goldcoast": "Gold Coast",
    "cairns-4ca": "Cairns",
    "cairns": "Cairns",
    "mildura": "Mildura",
    "portaugusta": "Port Augusta",
    "portlincoln": "Port Lincoln",
    "queenstown": "Queenstown",
    "demo-coast": "Demo Coast",
}


STATUS_NAMES = {
    "REQUESTED": "Requested",
    "HERE_FETCHED": "HereFetched",
    "INCIDENTS_SELECTED": "IncidentsSelected",
    "AWAITING_CURATION": "AwaitingCuration",
    "SCRIPT_READY": "ScriptReady",
    "SYNTHESIZING_AUDIO": "SynthesizingAudio",
    "AUDIO_READY": "AudioReady",
    "WAITING_APPROVAL": "WaitingApproval",
    "APPROVED": "Approved",
    "REJECTED": "Rejected",
    "PUBLISHED": "Published",
    "FALLBACK_USED": "FallbackUsed",
    "FAILED": "Failed",
    "MISSED": "Missed",
}


def token() -> str:
    proc = subprocess.run(
        [
            "az",
            "account",
            "get-access-token",
            "--resource",
            "https://management.azure.com",
            "--query",
            "accessToken",
            "-o",
            "tsv",
        ],
        check=False,
        capture_output=True,
        text=True,
    )
    if proc.returncode or not proc.stdout.strip():
        raise SystemExit(f"could not obtain Azure management token: {proc.stderr.strip()}")
    return proc.stdout.strip()


def get_json(url: str, bearer: str) -> Any:
    request = urllib.request.Request(url, headers={"Authorization": f"Bearer {bearer}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", "replace")
        raise SystemExit(f"GET {url} returned HTTP {exc.code}: {body[:500]}") from exc
    except urllib.error.URLError as exc:
        raise SystemExit(f"GET {url} failed: {exc.reason}") from exc


def parse_json_column(value: Any, default: Any) -> Any:
    if not value:
        return default
    if isinstance(value, (dict, list)):
        return value
    try:
        return json.loads(value)
    except (TypeError, json.JSONDecodeError):
        return default


def read_logs(job: dict[str, Any], cached: dict[str, Any] | None) -> list[dict[str, Any]]:
    logs = job.get("Logs")
    if isinstance(logs, list):
        return [x for x in logs if isinstance(x, dict)]
    if cached:
        logs = parse_json_column(cached.get("LogsJson"), [])
        if isinstance(logs, list):
            return [x for x in logs if isinstance(x, dict)]
    return []


def normalize_market_key(market: Any, export: dict[str, Any]) -> str | None:
    """Resolve a job market to one exact export key, with only documented aliases."""
    requested = str(market or "").strip()
    keys = {
        str(row.get("Market", "")).strip()
        for row in export.get("FillerStatements", [])
        if isinstance(row, dict) and str(row.get("Market", "")).strip()
    }
    keys.update(
        str(row.get("MarketId", "")).strip()
        for row in export.get("Schedules", [])
        if isinstance(row, dict) and str(row.get("MarketId", "")).strip()
    )
    for key in keys:
        if key.casefold() == requested.casefold():
            return key
    alias = MARKET_ALIASES.get(requested.casefold())
    if alias and any(key.casefold() == alias.casefold() for key in keys):
        print(f"[station-config] market '{requested}' mapped explicitly to export key '{alias}'")
        return next(key for key in keys if key.casefold() == alias.casefold())
    print(
        f"[station-config] WARNING: market '{requested}' has no exact or documented export match; "
        "leaving station config empty",
        file=sys.stderr,
    )
    return None


def resolve_bookend_text(template: Any, schedule: dict[str, Any], market_key: str) -> tuple[str, list[str]]:
    """Mirror BookendTokenResolver: built-ins, station overrides, then fail-closed tokens."""
    text = str(template or "")
    if not text.strip():
        return "", []
    place_name = MARKET_DISPLAY_NAMES.get(market_key.casefold(), "")
    values = {
        "city": place_name,
        "market": place_name,
        "station": str(schedule.get("Station") or "").strip(),
    }
    overrides = schedule.get("BookendTokens")
    if isinstance(overrides, dict):
        values.update(
            {str(k).strip().casefold(): str(v).strip() for k, v in overrides.items() if str(k).strip() and str(v).strip()}
        )
    unresolved: list[str] = []

    def replace(match: re.Match[str]) -> str:
        name = match.group(1)
        value = values.get(name.casefold(), "")
        if value:
            return value
        if name.casefold() not in {x.casefold() for x in unresolved}:
            unresolved.append(name)
        return ""

    resolved = re.sub(r"{([^{}]+)}", replace, text)
    return " ".join(resolved.split()), unresolved


def resolve_live_station_config(
    job: dict[str, Any], export: dict[str, Any]
) -> tuple[list[dict[str, Any]], dict[str, Any] | None, str | None]:
    """Resolve today's filler row and schedule-assigned bookend against the DEV export."""
    market_key = normalize_market_key(job.get("Market"), export)
    if market_key is None:
        return [], None, None

    fillers: list[dict[str, Any]] = []
    for row in export.get("FillerStatements", []):
        if not isinstance(row, dict) or str(row.get("Market", "")).casefold() != market_key.casefold():
            continue
        for statement in row.get("Statements", []):
            if not isinstance(statement, dict) or not statement.get("Id") or statement.get("Text") is None:
                print(
                    f"[station-config] WARNING: skipping malformed filler statement for '{market_key}'",
                    file=sys.stderr,
                )
                continue
            # Keep exactly the FillerStatement contract consumed by JsonDefaults.Storage.
            fillers.append(
                {
                    "Id": statement["Id"],
                    "Text": statement["Text"],
                    "Enabled": statement.get("Enabled", True),
                    "Availability": statement.get("Availability"),
                }
            )
        break

    schedule = next(
        (
            row
            for row in export.get("Schedules", [])
            if isinstance(row, dict)
            and str(row.get("MarketId", "")).casefold() == market_key.casefold()
        ),
        None,
    )
    bookends: dict[str, Any] | None = None
    if schedule:
        bookend_id = schedule.get("BookendId")
        rotation = schedule.get("BookendRotation")
        if not bookend_id and isinstance(rotation, dict):
            # Production's StationConfigResolver selects a rotation member before loading the
            # library. Config export omits the persisted cursor/time context; all current DEV
            # panel assignments are "single", but choose the first exported candidate rather
            # than guessing a different pair if a future capture encounters a rotation.
            candidates = rotation.get("BookendIds")
            if isinstance(candidates, list) and candidates:
                bookend_id = candidates[0]
        if bookend_id:
            library_row = next(
                (
                    row
                    for row in export.get("Bookends", [])
                    if isinstance(row, dict) and row.get("Id") == bookend_id
                ),
                None,
            )
            if library_row is None:
                print(
                    f"[station-config] WARNING: schedule '{market_key}' references missing bookend "
                    f"'{bookend_id}'; leaving pair empty",
                    file=sys.stderr,
                )
            else:
                intro, intro_missing = resolve_bookend_text(
                    library_row.get("IntroText"), schedule, str(schedule.get("MarketId") or market_key)
                )
                outro, outro_missing = resolve_bookend_text(
                    library_row.get("OutroText"), schedule, str(schedule.get("MarketId") or market_key)
                )
                unresolved = intro_missing + [x for x in outro_missing if x.casefold() not in {y.casefold() for y in intro_missing}]
                if unresolved:
                    print(
                        f"[station-config] WARNING: bookend '{bookend_id}' for '{market_key}' has "
                        f"unresolved token(s) {', '.join(unresolved)}; leaving pair empty",
                        file=sys.stderr,
                    )
                elif intro or outro:
                    # Config export intentionally omits Bookend.CurrentVersion. Version 0 is an
                    # explicit unknown-version sentinel; production replay consumes the resolved
                    # text/id, while provenance must not mistake this for version one (2026-08-23).
                    bookends = {
                        "IntroText": intro,
                        "OutroText": outro,
                        "Id": bookend_id,
                        "Version": 0,
                    }
    if not fillers and bookends is None:
        print(f"[station-config] '{market_key}' has no resolvable filler statements or bookend assignment")
    return fillers, bookends, market_key


def messages(logs: list[dict[str, Any]]) -> list[str]:
    return [str(x.get("Message", "")) for x in logs]


def parse_outcome(job: dict[str, Any], logs: list[dict[str, Any]]) -> dict[str, Any]:
    text = "\n".join(messages(logs))
    targets = [int(x) for x in re.findall(r"Generation attempt\s+\d+(?:/\d+)?:\s*target\s+(\d+)\s+words", text, re.I)]
    synth_words: list[int | None] = []
    for match in re.finditer(r"Synthesizing audio\s*\((\d+)\s+words?", text, re.I):
        synth_words.append(int(match.group(1)))
    natural: list[float | None] = []
    for match in re.finditer(r"Audio filled to slot:\s*([0-9]+(?:\.[0-9]+)?)s\s+natural", text, re.I):
        natural.append(float(match.group(1)))
    # Keep all sequences aligned when a synthesis attempt failed before it could emit a
    # duration. The logs don't always carry an explicit attempt number, so the safest
    # alignment is positional and missing values are represented as null.
    count = max(len(targets), len(synth_words), len(natural))
    if len(synth_words) < count:
        synth_words.extend([None] * (count - len(synth_words)))
    if len(natural) < count:
        natural.extend([None] * (count - len(natural)))
    if len(targets) < count:
        # Re-fitting attempts do not repeat a numeric target in their log line. The
        # synthesized word count is the only honest target available for that attempt;
        # never emit zero, which the replay interprets as a real target.
        for index in range(len(targets), count):
            inferred = synth_words[index]
            if inferred is None and targets:
                inferred = targets[-1]
            targets.append(int(inferred or 0))
    best = max((x for x in natural if x is not None), default=None)
    feedback = len(re.findall(r"Review feedback", text, re.I))
    failures = [
        m.strip()
        for m in messages(logs)
        if re.search(r"(?:synth(?:esis|esizing)|audio)\s+(?:failed|failure|error)", m, re.I)
    ]
    status = STATUS_NAMES.get(str(job.get("Status", "")).upper(), str(job.get("Status", "")))
    fallback = job.get("Fallback") if isinstance(job.get("Fallback"), dict) else {}
    fallback_kind = fallback.get("Reason") or next(
        (m.rstrip(".") + "." for m in messages(logs) if "fallback" in m.lower()), None
    )
    return {
        "Status": status,
        "FallbackKind": fallback_kind,
        "Attempts": len(targets),
        "TargetWordsSeq": targets,
        "SynthWordsSeq": synth_words,
        "NaturalSecSeq": natural,
        "BestNaturalSec": best,
        "ReviewFeedbackCount": feedback,
        "SynthFailures": failures,
    }


def incident_from_api(item: dict[str, Any]) -> dict[str, Any]:
    headline = str(item.get("Headline") or "")
    location = headline.split(" on ", 1)[1] if " on " in headline else headline
    return {
        "Id": str(item.get("Key") or ""),
        "Description": headline,
        "Severity": int(item.get("Priority") or 0),
        "Location": location,
        "Type": item.get("Type") or "unknown",
        "LengthMeters": float(item.get("LengthMeters") or 0),
        "FunctionalClass": int(item.get("FunctionalClass") or 0),
        "Latitude": item.get("Latitude"),
        "Longitude": item.get("Longitude"),
        "IsNew": bool(item.get("IsNew", False)),
        "DataTimestampUtc": None,
        "Direction": item.get("Direction"),
        "Summary": item.get("Detail"),
        "JunctionTraversability": None,
        "SegmentRefs": None,
        "Comment": item.get("Comment"),
        "Shape": item.get("Shape"),
        "RoadClosed": bool(item.get("RoadClosed", False)),
        "EntryTimeUtc": item.get("EntryTimeUtc"),
    }


def snapshot_from_detail(job: dict[str, Any], cached: dict[str, Any] | None) -> tuple[dict[str, Any], str]:
    stored = None
    if cached:
        stored = parse_json_column(cached.get("GeneratorInputJson"), None)
    stored = stored or job.get("GeneratorInput") or job.get("Snapshot")
    if isinstance(stored, dict):
        snapshot = dict(stored)
        snapshot.pop("MarketFeedback", None)  # retired field accepted by old fixtures only
        source = "job-snapshot"
    else:
        traffic = job.get("Traffic") if isinstance(job.get("Traffic"), dict) else {}
        incidents = {
            str(x.get("Key")): incident_from_api(x)
            for x in traffic.get("Incidents", [])
            if isinstance(x, dict) and x.get("Key") is not None
        }
        corridors = []
        for row in traffic.get("Corridors", []):
            if not isinstance(row, dict):
                continue
            keys = row.get("IncidentKeys") or []
            corridors.append(
                {
                    "RoadName": row.get("RoadName", ""),
                    "Severity": int(row.get("Severity") or 0),
                    "FunctionalClass": int(row.get("FunctionalClass") or 0),
                    "TotalLengthMeters": float(row.get("TotalLengthKm") or 0) * 1000,
                    "ImpactScore": float(row.get("ImpactScore") or 0),
                    "PrimaryType": row.get("PrimaryType") or "unknown",
                    "Description": row.get("Description") or "",
                    # The job DTO does not expose DescriptionIsDerived. A pure-congestion
                    # corridor's description ("crawling", "at a standstill") is ALWAYS our own
                    # speed-ratio band, never an observation, and the evidence builder suppresses
                    # derived descriptions; without this flag a replay leaks severity vocabulary
                    # the live pipeline never shows its writers (observed 2026-08-27).
                    "DescriptionIsDerived": (row.get("PrimaryType") or "") == "congestion",
                    "Incidents": [incidents[k] for k in keys if str(k) in incidents],
                    "Suburb": row.get("Suburb"),
                    "Direction": row.get("Direction"),
                    "Extent": None,
                    "DriveTimeMinutes": row.get("DriveTimeMinutes"),
                    "NormalDriveTimeMinutes": row.get("NormalDriveTimeMinutes"),
                    "Latitude": row.get("Latitude"),
                    "Longitude": row.get("Longitude"),
                    "StartPoint": None,
                    "EndPoint": None,
                    "JamTendency": None,
                    "FlowDelayMinutes": row.get("DelayMinutes"),
                    "PriorityBoosted": bool(row.get("PriorityBoosted", False)),
                    "BaseScore": row.get("BaseScore"),
                    "BoostAmount": row.get("BoostAmount"),
                    "Detour": None,
                    "MaxObservationMeters": None,
                    "HotSpotMeters": None,
                    "Sources": row.get("Sources"),
                    "OfficialHeadline": row.get("OfficialHeadline"),
                    "OfficialCrossStreet": row.get("OfficialCrossStreet"),
                    "OfficialEvidences": row.get("OfficialEvidences"),
                }
            )
        flow = []
        for row in traffic.get("FlowRoads", []):
            if not isinstance(row, dict):
                continue
            flow.append(
                {
                    "RoadName": row.get("RoadName", ""),
                    "JamFactor": row.get("JamFactor", 0),
                    "LengthMeters": float(row.get("LengthKm") or 0) * 1000,
                    "IsWeighted": bool(row.get("IsWeighted", False)),
                    "FunctionalClass": int(row.get("FunctionalClass") or 0),
                    "Score": row.get("Score", 0),
                    "PriorityBoosted": bool(row.get("PriorityBoosted", False)),
                }
            )
        content = job.get("Content") if isinstance(job.get("Content"), dict) else {
            "Traffic": True, "Weather": True, "Fuel": True,
            "WeatherRequired": False, "FuelRequired": False,
        }
        snapshot = {
            "TargetDurationSeconds": job.get("TargetDurationSec", 0),
            "Location": job.get("Market", ""),
            "City": job.get("Market"),
            "MarketId": job.get("Market"),
            "Content": content,
            "Bookends": None,
            "FillerStatements": [],
            "SharedRules": None,
            "RoadFeelPalette": None,
            "EditorialSettings": {"Scope": 0, "ScopeId": ""},
            "CuratedCorridorRoads": None,
            "CuratedGoodRunRoads": None,
            "ExcludedIncidentKeys": None,
            "WriterPrompt": None,
            "FillOrder": ["Weather", "Fuel", "MinorTraffic", "Filler"],
            "Corridors": corridors,
            "MonitoredRoutes": [],
            "GoodRunRoads": job.get("GoodRuns") or [],
            "FlowPool": flow,
            "Weather": job.get("Weather"),
            "Fuel": job.get("Fuel"),
            "IncidentChanges": None,
            "Guidance": {"Entries": [], "DroppedCount": 0},
            "EditedIncidents": [],
            "FutureLessons": [],
        }
        source = "none"
    # New station-config fields are deliberately present even when the old persisted job did
    # not have them. Their safe defaults let old fixtures deserialize and tell replay exactly
    # which values must be reconstructed from the live station config.
    defaults = {
        "Bookends": None,
        "FillerStatements": [],
        "SharedRules": None,
        "RoadFeelPalette": None,
        "EditorialSettings": {"Scope": 0, "ScopeId": ""},
        "CuratedCorridorRoads": None,
        "CuratedGoodRunRoads": None,
        "ExcludedIncidentKeys": None,
        "WriterPrompt": None,
        "FillOrder": ["Weather", "Fuel", "MinorTraffic", "Filler"],
        "EditedIncidents": [],
        "FutureLessons": [],
    }
    for key, value in defaults.items():
        snapshot.setdefault(key, value)
    return snapshot, source


def voice_config(job: dict[str, Any], cached: dict[str, Any] | None) -> dict[str, Any]:
    if cached:
        vc = parse_json_column(cached.get("VoiceConfigJson"), None)
        if isinstance(vc, dict):
            return vc
    voice = job.get("Input", {}).get("Voice", {}) if isinstance(job.get("Input"), dict) else {}
    return {
        "Provider": voice.get("Provider", ""),
        "VoiceId": voice.get("VoiceId", ""),
        "VoiceName": voice.get("VoiceName"),
        "Model": voice.get("Model"),
        "Settings": voice.get("Settings"),
        "PaceBoostWpm": 0,
    }

def capture(
    job: dict[str, Any],
    cached: dict[str, Any] | None,
    station_export: dict[str, Any] | None = None,
) -> dict[str, Any]:
    snapshot, source = snapshot_from_detail(job, cached)
    config_keys = (
        "Bookends",
        "FillerStatements",
        "SharedRules",
        "RoadFeelPalette",
        "CuratedCorridorRoads",
        "CuratedGoodRunRoads",
        "ExcludedIncidentKeys",
        "WriterPrompt",
    )
    has_snapshot_config = any(snapshot.get(key) not in (None, [], "") for key in config_keys)
    if station_export is not None and not (source == "job-snapshot" and has_snapshot_config):
        fillers, bookends, market_key = resolve_live_station_config(job, station_export)
        snapshot["FillerStatements"] = fillers
        snapshot["Bookends"] = bookends
        source = "live-station-config" if market_key and (fillers or bookends) else "none"
        print(
            f"[station-config] market='{job.get('Market', '')}' export='{market_key or '-'}' "
            f"fillers={len(fillers)} bookends={'yes' if bookends else 'no'} source={source}"
        )
    incidents = sum(len(c.get("Incidents", [])) for c in snapshot.get("Corridors", []) if isinstance(c, dict))
    fidelity = {
        "IncidentsRecovered": incidents,
        "IncidentsSynthesized": 0,
        "WeatherSource": "job" if job.get("Weather") else "off",
        "FuelSource": "job" if job.get("Fuel") else "off",
        "GoodRunsAndFlowPoolLost": False,
        "StationConfigSource": source if any(snapshot.get(k) not in (None, [], "") for k in (
            "Bookends", "FillerStatements", "SharedRules", "RoadFeelPalette", "CuratedCorridorRoads",
            "CuratedGoodRunRoads", "ExcludedIncidentKeys", "WriterPrompt")) else "none",
    }
    outcome = parse_outcome(job, read_logs(job, cached))
    return {
        "JobId": job.get("JobId"),
        "Market": job.get("Market", ""),
        "Station": job.get("Station"),
        "CapturedAtUtc": job.get("CreatedAt"),
        "DevOutcome": outcome,
        "Fidelity": fidelity,
        "VoiceConfig": voice_config(job, cached),
        "Snapshot": snapshot,
        "Coverage": job.get("Coverage"),
        "FullCorridorPool": snapshot.get("Corridors", []),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("job_id", help="DEV job id")
    parser.add_argument("--api", default=DEFAULT_API, help=f"read-only jobs API base (default: {DEFAULT_API})")
    parser.add_argument("--out", type=pathlib.Path, help="fixture path (default: test/corpus/fallback-replay/<id>.json)")
    parser.add_argument("--snapshot-file", type=pathlib.Path, help="local persisted radioJobs row with GeneratorInputJson")
    parser.add_argument("--job-file", type=pathlib.Path, help="pre-fetched detail JSON (skips the jobs API; config export is still read)")
    args = parser.parse_args()

    cached = json.loads(args.snapshot_file.read_text()) if args.snapshot_file else None
    bearer = token()
    if args.job_file:
        detail = json.loads(args.job_file.read_text())
    else:
        # The list call is an intentional existence/polarity check before the full detail read;
        # detail remains authoritative because it carries Logs, Traffic and terminal metadata.
        get_json(f"{args.api}/jobs?limit=500", bearer)
        detail = get_json(f"{args.api}/jobs/{args.job_id}", bearer)
    # Historical jobs do not persist station filler/bookend config. This is deliberately a
    # separate read-only export call, even with --job-file, so a local detail copy still gains
    # today's DEV station configuration rather than synthetic defaults.
    station_export = get_json(
        CONFIG_EXPORT_URL if args.api == DEFAULT_API else f"{args.api}/config/export",
        bearer,
    )
    if not isinstance(station_export, dict):
        raise SystemExit("GET /api/config/export returned a non-object payload")
    if detail.get("JobId") != args.job_id:
        raise SystemExit(f"requested {args.job_id}, API returned {detail.get('JobId')}")
    fixture = capture(detail, cached, station_export)
    out = args.out or OUT_ROOT / f"{args.job_id}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(fixture, indent=1, ensure_ascii=False) + "\n")
    print(f"wrote {out} ({fixture['Market']}, {fixture['Snapshot']['TargetDurationSeconds']}s, {fixture['DevOutcome']['Status']})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
