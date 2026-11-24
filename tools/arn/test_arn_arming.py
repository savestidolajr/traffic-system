#!/usr/bin/env python3
"""
Tests for `arn trigger --via poller`'s schedule arming (`_arm_payload`, `_minutes_until`).

These guard a defect that cost 16 minutes of silence on test/sydney (2026-08-24): a schedule
carrying a Calendar ignores CronExpression on every date an assignment covers, so arming the
base cron was a no-op and the CLI simply waited out its timeout with no explanation.

Following the convention set by scripts/test_export_prompts.py, this file is self-contained
and needs no network, Azure CLI or local host. Run:

    python3 -m pytest tools/arn/test_arn_arming.py -v

or, without pytest:

    python3 tools/arn/test_arn_arming.py
"""

from __future__ import annotations

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parent))

import arn  # noqa: E402

SYD = ZoneInfo("Australia/Sydney")
# 14:47 UTC on 24 Aug is 00:47 on the 25th in Sydney — the station-local date the arm must
# target is the 25th, not the UTC date. This is the exact case that misfired on test.
TARGET = datetime(2026, 8, 25, 0, 58, tzinfo=SYD)

CALENDAR = {
    "Patterns": [
        {"Id": "day-type", "Name": "CALENDAR TEST", "CronExpression": "0 6,7,8 * * *"},
    ],
    "Assignments": [
        {"StartDate": "2026-08-26", "EndDate": "2026-08-26",
         "PatternId": "day-type", "Suppress": False, "Note": None},
        {"StartDate": "2026-08-25", "EndDate": "2026-08-25",
         "PatternId": "day-type", "Suppress": False, "Note": None},
    ],
}


def _arm_rows(payload):
    cal = payload["Calendar"]
    patterns = [p for p in cal["Patterns"] if p["Id"] == arn.ARM_PATTERN_ID]
    assignments = [a for a in cal["Assignments"] if a["PatternId"] == arn.ARM_PATTERN_ID]
    return patterns, assignments


def test_no_calendar_arms_the_base_cron():
    payload, how = arn._arm_payload({"CronExpression": "0 6 * * 1-5"}, TARGET)
    assert payload == {"CronExpression": "58 0 * * *", "Enabled": True}
    assert how == "base cron"


def test_calendar_is_armed_through_a_single_date_assignment():
    payload, how = arn._arm_payload({"CronExpression": "0 6 * * 1-5", "Calendar": CALENDAR}, TARGET)
    assert how == "calendar assignment for 2026-08-25"
    assert payload["Enabled"] is True
    # The base cron must be left alone: on a covered date it is not what the resolver reads.
    assert "CronExpression" not in payload
    patterns, assignments = _arm_rows(payload)
    assert [p["CronExpression"] for p in patterns] == ["58 0 * * *"]
    assert len(assignments) == 1
    # A single-date window is the shortest possible, and ResolveDay breaks an exact tie in
    # favour of the LATER list entry — so being last is what makes this row win.
    assert assignments[0]["StartDate"] == assignments[0]["EndDate"] == "2026-08-25"
    assert assignments[0]["Suppress"] is False
    assert payload["Calendar"]["Assignments"][-1] is assignments[0]


def test_arming_preserves_the_operators_own_calendar_rows():
    payload, _ = arn._arm_payload({"CronExpression": "0 6 * * *", "Calendar": CALENDAR}, TARGET)
    cal = payload["Calendar"]
    assert CALENDAR["Patterns"][0] in cal["Patterns"]
    for assignment in CALENDAR["Assignments"]:
        assert assignment in cal["Assignments"]
    # Nothing may be dropped: the operator's rows plus exactly one arm row.
    assert len(cal["Patterns"]) == len(CALENDAR["Patterns"]) + 1
    assert len(cal["Assignments"]) == len(CALENDAR["Assignments"]) + 1


def test_a_stale_arm_row_is_replaced_not_duplicated():
    """An interrupted run leaves its arm row behind; the next arm must not stack another."""
    polluted = {
        "Patterns": CALENDAR["Patterns"] + [
            {"Id": arn.ARM_PATTERN_ID, "Name": "arn CLI arm (temporary)",
             "CronExpression": "13 4 * * *"},
        ],
        "Assignments": CALENDAR["Assignments"] + [
            {"StartDate": "2026-08-19", "EndDate": "2026-08-19",
             "PatternId": arn.ARM_PATTERN_ID, "Suppress": False},
        ],
    }
    payload, _ = arn._arm_payload({"CronExpression": "0 6 * * *", "Calendar": polluted}, TARGET)
    patterns, assignments = _arm_rows(payload)
    assert len(patterns) == 1 and len(assignments) == 1
    assert patterns[0]["CronExpression"] == "58 0 * * *"
    assert assignments[0]["StartDate"] == "2026-08-25"


def test_a_suppressed_target_date_is_still_armable():
    """Suppression is just another covering assignment, so the arm row must still beat it."""
    suppressed = {
        "Patterns": CALENDAR["Patterns"],
        "Assignments": [{"StartDate": "2026-08-25", "EndDate": "2026-08-25",
                         "PatternId": None, "Suppress": True}],
    }
    payload, _ = arn._arm_payload({"CronExpression": "0 6 * * *", "Calendar": suppressed}, TARGET)
    _, assignments = _arm_rows(payload)
    assert len(assignments) == 1
    assert payload["Calendar"]["Assignments"][-1]["PatternId"] == arn.ARM_PATTERN_ID


def test_minutes_until_parses_the_servers_next_run_shapes():
    now = datetime.now(timezone.utc)
    soon = (now + timedelta(minutes=14)).strftime("%Y-%m-%dT%H:%M:%SZ")
    assert 13 < arn._minutes_until(soon) <= 14
    offset = (now + timedelta(minutes=14)).strftime("%Y-%m-%dT%H:%M:%S+00:00")
    assert 13 < arn._minutes_until(offset) <= 14
    # A naive instant is the server's UTC, not local time — misreading it would let an arm
    # that did not take look due.
    naive = (now + timedelta(minutes=14)).strftime("%Y-%m-%dT%H:%M:%S")
    assert 13 < arn._minutes_until(naive) <= 14


def test_minutes_until_rejects_what_it_cannot_read():
    for bad in (None, "", "not-a-date", 42, {}):
        assert arn._minutes_until(bad) is None


def test_minutes_until_is_negative_for_a_past_instant():
    past = (datetime.now(timezone.utc) - timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")
    assert arn._minutes_until(past) < 0


if __name__ == "__main__":
    failures = 0
    for name, fn in sorted(globals().items()):
        if not name.startswith("test_") or not callable(fn):
            continue
        try:
            fn()
            print(f"PASS {name}")
        except AssertionError as exc:
            failures += 1
            print(f"FAIL {name}: {exc}")
    print(f"\n{'FAILED' if failures else 'OK'} — {failures} failure(s)")
    sys.exit(1 if failures else 0)
