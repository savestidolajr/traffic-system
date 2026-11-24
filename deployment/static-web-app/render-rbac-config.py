#!/usr/bin/env python3
"""Render the test/prod SWA auth config with that environment's rolesSource token audience.

WHY THIS EXISTS
---------------
`auth.rolesSource` (POST /api/GetRoles) is the only moment Static Web Apps sends the
signed-in user's real Entra `oid` to the backend, and `SwaRolesSourceTokenValidator`
refuses to persist anything from a token it cannot cryptographically validate. Its
`ValidAudiences` are `[<clientId>, api://<clientId>]`.

With only OIDC scopes requested (`openid profile email`), Entra defaults the ACCESS
token's resource to Microsoft Graph, so the token arrives with
`aud=00000003-0000-0000-c000-000000000000`. That can never be validated by this app,
so every sign-in was discarded and no operator row was ever written on test or prod.

Asking for `<clientId>/.default` alongside the OIDC scopes makes Entra mint the access
token for THIS app, giving `aud=<clientId>` — the first entry in ValidAudiences.

Measured against Entra 2026-08-13 (authorize probe, TEST registration):
  `<clientId>/.default`      -> code issued, scope accepted
  `api://<clientId>/.default -> AADSTS500011 invalid_resource (no identifierUri exists)
So the bare client id is the only form that works; do NOT "tidy" it into an api:// URI
without re-running that probe.

The client id is per environment, and `loginParameters` values are literals — SWA
expands `clientIdSettingName` from app settings but NOT the contents of
`loginParameters`. That is the whole reason this renderer exists instead of a second
committed config file: one canonical config, no drift between test and prod.

SAFE BY OMISSION: the canonical config ships the OIDC scopes only, which is exactly
today's effective behaviour. A deploy path that copies it directly instead of calling
this renderer therefore degrades to "no operator self-registration" — never to a
broken sign-in.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CANONICAL = REPO_ROOT / "deployment" / "static-web-app" / "staticwebapp.rbac.config.json"
BASE_SCOPES = "openid profile email"
GUID = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")


def param_file_for(env: str) -> Path:
    return REPO_ROOT / "deployment" / f"main.{env}.bicepparam"


def client_id_from(param: Path) -> str:
    """The env's app registration, read from the SAME bicepparam that provisions the
    Function App's `AadClientId` — so the console and the backend can never disagree
    about which registration the rolesSource token must be addressed to."""
    if not param.is_file():
        sys.exit(f"render-rbac-config: no such parameter file: {param}")
    match = re.search(r"^\s*param\s+aadClientId\s*=\s*'([^']+)'", param.read_text(), re.MULTILINE)
    if not match:
        sys.exit(f"render-rbac-config: no `param aadClientId` in {param}")
    client_id = match.group(1).strip()
    # A `<placeholder>` is the documented fail-closed state for an unprovisioned env; a
    # non-GUID would reach Entra as an invalid scope and break sign-in for everyone.
    if not GUID.match(client_id):
        sys.exit(f"render-rbac-config: aadClientId in {param.name} is not a GUID: {client_id!r}")
    return client_id


def render(param: Path) -> dict:
    config = json.loads(CANONICAL.read_text())
    provider = config["auth"]["identityProviders"]["azureActiveDirectory"]
    scope = f"{BASE_SCOPES} {client_id_from(param)}/.default"
    provider["login"] = {"loginParameters": [f"scope={scope}"]}
    return config


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--env", choices=["test", "prod"],
                        help="resolve deployment/main.<env>.bicepparam")
    source.add_argument("--param-file",
                        help="the bicepparam to read aadClientId from (what the pipeline passes)")
    parser.add_argument("dest", nargs="?", help="output path; omitted prints to stdout")
    parser.add_argument("--check", action="store_true",
                        help="render and assert the audience scope is present; write nothing")
    args = parser.parse_args()

    param = Path(args.param_file) if args.param_file else param_file_for(args.env)
    if not param.is_absolute():
        param = (REPO_ROOT / param) if not param.exists() else param
    label = args.env or param.name

    config = render(param)
    params = config["auth"]["identityProviders"]["azureActiveDirectory"]["login"]["loginParameters"]
    scope = params[0]

    # Assertions run on EVERY render, not only --check: a deploy must fail loudly here
    # rather than ship a config that silently discards every sign-in.
    if not scope.startswith("scope="):
        sys.exit(f"render-rbac-config: malformed loginParameter: {scope!r}")
    if "/.default" not in scope:
        sys.exit(f"render-rbac-config: {label}: audience scope missing — sign-in would not register")
    if "api://" in scope:
        sys.exit(f"render-rbac-config: {label}: api:// form is AADSTS500011 against these registrations")

    if args.check:
        print(f"[OK] {label}: {scope}")
        return 0

    rendered = json.dumps(config, indent=2) + "\n"
    if args.dest:
        Path(args.dest).write_text(rendered)
        print(f"rendered {label} SWA auth config -> {args.dest}")
        print(f"  rolesSource token audience: {scope}")
    else:
        sys.stdout.write(rendered)
    return 0


if __name__ == "__main__":
    sys.exit(main())
