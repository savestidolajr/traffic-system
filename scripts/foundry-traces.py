#!/usr/bin/env python3
"""
Foundry agent trace viewer — a simple programmatic interface to the LLM-call
traces that back the Microsoft Foundry portal "Traces" tab, without the SPA.

The Foundry Agent Service exports each agent LLM call to the project's connected
Application Insights as a `dependencies` span named `invoke_agent <agent>:<ver>`,
carrying the full prompt/completion in customDimensions (OpenTelemetry gen_ai
semantic conventions). This tool queries that store over the App Insights REST
query API (reliable + scriptable; the `az monitor app-insights query` wrapper
returns empty intermittently).

USAGE
  scripts/foundry-traces.py list  [--hours 2] [--agent draft|polish|review|refine-sentence|attribute|safety|director] [--limit 40] [--env dev]
  scripts/foundry-traces.py flow  <operation_id> [--full] [--grep <text>] [--chars 1200] [--hours 6]
  scripts/foundry-traces.py call  --agent draft [--hours 2]      # newest single call of one agent, full I/O

Prereqs: `az login` with reader on the App Insights (IBP Sub). Token audience is
https://api.applicationinsights.io.

CAVEAT: these spans are subject to App Insights sampling/retention — reliably
present for ~1-2h after a run, then thinned. For older runs, widen --hours (data
may still be gone). operation_Id groups a whole SchedulePoller tick, so one
`flow` can interleave several markets — use --grep <road/market> to isolate one.
"""
import argparse, json, subprocess, sys, urllib.parse, urllib.request, time

# Connected App Insights (appId) per environment. dev = IBP-AUE-DEV-AIN-APP01.
APPIDS = {
    "dev":  "7a5d2749-30d0-47ce-8a73-708251f2a340",
    "test": "37e06f88-8489-4516-81f2-08f80a2f39b9",
    "prod": "067be6fd-48c3-4110-ac8d-8101a05e7228",
}

def token():
    r = subprocess.run(
        ["az", "account", "get-access-token", "--resource",
         "https://api.applicationinsights.io", "--query", "accessToken", "-o", "tsv"],
        capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("az token failed (run `az login`): " + r.stderr.strip())
    return r.stdout.strip()

def query(appid, kql, tok, tries=4):
    url = f"https://api.applicationinsights.io/v1/apps/{appid}/query?" + \
          urllib.parse.urlencode({"query": kql})
    last = None
    for _ in range(tries):
        try:
            req = urllib.request.Request(url, headers={"Authorization": f"Bearer {tok}"})
            with urllib.request.urlopen(req, timeout=60) as resp:
                d = json.loads(resp.read())
            t = d["tables"][0]
            cols = [c["name"] for c in t["columns"]]
            return [dict(zip(cols, row)) for row in t["rows"]]
        except Exception as e:
            last = e; time.sleep(2)
    sys.exit(f"query failed: {last}")

def render_messages(raw, chars):
    """Render gen_ai.*.messages JSON into 'role: text' lines."""
    try:
        msgs = json.loads(raw)
    except Exception:
        return (raw or "")[:chars]
    out = []
    for m in msgs:
        role = m.get("role", "?")
        parts = m.get("parts") or []
        text = " ".join(p.get("content", "") for p in parts if isinstance(p, dict))
        if not text and isinstance(m.get("content"), str):
            text = m["content"]
        out.append(f"  [{role}] {text.strip()[:chars]}")
    return "\n".join(out)

def cmd_list(a, appid, tok):
    flt = f"| where name has '{a.agent}'" if a.agent else ""
    kql = (f"dependencies | where timestamp>ago({a.hours}h) "
           f"| where name startswith 'invoke_agent' {flt} "
           f"| project timestamp, agent=tostring(name), dur=duration, op=operation_Id, "
           f"model=tostring(customDimensions['gen_ai.request.model']), "
           f"out=tostring(customDimensions['gen_ai.output.messages']) "
           f"| order by timestamp desc | take {a.limit}")
    rows = query(appid, kql, tok)
    if not rows:
        print(f"(no invoke_agent spans in last {a.hours}h — widen --hours; note ~1-2h retention)"); return
    print(f"{'time(UTC)':10} {'agent':32} {'dur':>7} {'model':26} operation_Id")
    for r in rows:
        snippet = ""
        try:
            m = json.loads(r["out"]); snippet = (m[-1]["parts"][0]["content"] or "")[:60].replace("\n", " ")
        except Exception:
            pass
        print(f"{r['timestamp'][11:19]:10} {r['agent']:32} {r['dur']:>6.0f}m {str(r.get('model') or ''):26} {r['op']}")
        if snippet:
            print(f"           └─ {snippet}…")

def cmd_flow(a, appid, tok):
    grep = f"| where customDimensions has '{a.grep}'" if a.grep else ""
    kql = (f"dependencies | where timestamp>ago({a.hours}h) "
           f"| where operation_Id=='{a.op}' | where name startswith 'invoke_agent' {grep} "
           f"| project timestamp, agent=tostring(name), dur=duration, "
           f"model=tostring(customDimensions['gen_ai.request.model']), "
           f"inp=tostring(customDimensions['gen_ai.input.messages']), "
           f"outp=tostring(customDimensions['gen_ai.output.messages']) "
           f"| order by timestamp asc")
    rows = query(appid, kql, tok)
    if not rows:
        print(f"(no agent calls for operation {a.op} in last {a.hours}h)"); return
    print(f"# Foundry trace {a.op} — {len(rows)} agent call(s)")
    for i, r in enumerate(rows, 1):
        print(f"\n=== [{i}] {r['agent']}  {r['dur']:.0f}ms  {r.get('model') or ''} ===")
        if a.full:
            print("INPUT:"); print(render_messages(r["inp"], a.chars))
            print("OUTPUT:"); print(render_messages(r["outp"], a.chars))
        else:
            print(render_messages(r["outp"], a.chars))

def cmd_call(a, appid, tok):
    kql = (f"dependencies | where timestamp>ago({a.hours}h) "
           f"| where name startswith 'invoke_agent {a.agent_full}' "
           f"| top 1 by timestamp desc "
           f"| project timestamp, agent=tostring(name), dur=duration, op=operation_Id, "
           f"model=tostring(customDimensions['gen_ai.request.model']), "
           f"inp=tostring(customDimensions['gen_ai.input.messages']), "
           f"outp=tostring(customDimensions['gen_ai.output.messages'])")
    rows = query(appid, kql, tok)
    if not rows:
        print(f"(no {a.agent_full} call in last {a.hours}h)"); return
    r = rows[0]
    print(f"# {r['agent']}  {r['timestamp']}  {r['dur']:.0f}ms  {r.get('model') or ''}  op={r['op']}")
    print("\n--- INPUT ---"); print(render_messages(r["inp"], a.chars))
    print("\n--- OUTPUT ---"); print(render_messages(r["outp"], a.chars))

AGENT_PREFIX = {"draft": "arn-draft", "review": "arn-review",
                "refine-sentence": "arn-refine-sentence", "safety": "arn-safety",
                "director": "arn-director"}

def main():
    p = argparse.ArgumentParser(description="Foundry agent trace viewer (App Insights REST).")
    p.add_argument("--env", default="dev", choices=list(APPIDS))
    p.add_argument("--appid", help="override App Insights appId")
    sub = p.add_subparsers(dest="cmd", required=True)

    pl = sub.add_parser("list", help="recent agent LLM calls")
    pl.add_argument("--hours", type=float, default=2); pl.add_argument("--limit", type=int, default=40)
    pl.add_argument("--agent", choices=list(AGENT_PREFIX), help="filter by agent")

    pf = sub.add_parser("flow", help="all agent calls in one trace (operation_Id)")
    pf.add_argument("op"); pf.add_argument("--hours", type=float, default=6)
    pf.add_argument("--full", action="store_true", help="show input + output"); pf.add_argument("--grep")
    pf.add_argument("--chars", type=int, default=1200)

    pc = sub.add_parser("call", help="newest single call of one agent, full I/O")
    pc.add_argument("--agent", required=True, choices=list(AGENT_PREFIX))
    pc.add_argument("--hours", type=float, default=2); pc.add_argument("--chars", type=int, default=4000)

    a = p.parse_args()
    appid = a.appid or APPIDS[a.env]
    tok = token()
    if a.cmd == "list":
        cmd_list(a, appid, tok)
    elif a.cmd == "flow":
        a.chars = getattr(a, "chars", 1200); cmd_flow(a, appid, tok)
    elif a.cmd == "call":
        a.agent_full = AGENT_PREFIX[a.agent]; cmd_call(a, appid, tok)

if __name__ == "__main__":
    main()
