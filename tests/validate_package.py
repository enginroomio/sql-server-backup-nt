#!/usr/bin/env python3
"""Mac/Linux/Windows: package structure + config/SQL/script sanity (no SQL Server required)."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
ok: list[str] = []


def check(cond: bool, msg: str) -> None:
    if cond:
        ok.append(msg)
    else:
        errors.append(msg)


def main() -> int:
    required = [
        "README.md",
        "basit-ayarlar.json",
        "backup.master.sql",
        "setup.bat",
        "setup.ps1",
        "Kurulum-Sunucu.bat",
        "setup-server.ps1",
        "Uygula-Ayarlar.ps1",
        "src/Program.cs",
        "src/Core.cs",
        "src/BackupEngine.cs",
        "src/SQLYedeklemeAraci.csproj",
        "src/ServerUi.cs",
        ".github/workflows/windows-sql-smoke.yml",
    ]
    for rel in required:
        check((ROOT / rel).is_file(), f"exists: {rel}")

    # basit-ayarlar.json — exactly the 4 end-user knobs
    simple = json.loads((ROOT / "basit-ayarlar.json").read_text(encoding="utf-8"))
    for key in ("Sunucu", "YedekKlasoru", "SaklamaAdedi", "Saat"):
        check(key in simple, f"basit-ayarlar has {key}")
    check(isinstance(simple.get("SaklamaAdedi"), int) and 1 <= simple["SaklamaAdedi"] <= 365, "SaklamaAdedi in 1..365")
    check(bool(re.fullmatch(r"\d{1,2}:\d{2}", str(simple.get("Saat", "")))), "Saat looks like HH:mm")

    sql = (ROOT / "backup.master.sql").read_text(encoding="utf-8", errors="replace")
    for token in ("$(StagingDir)", "$(Timestamp)", "$(OnlyDb)", "BACKUP DATABASE", "RESTORE VERIFYONLY", "QUOTENAME"):
        check(token in sql, f"SQL contains {token}")
    # No hardcoded production passwords in SQL script
    check("Guclu-Bir-Parola" not in sql, "SQL has no sample hardcoded password")

    core = (ROOT / "src/Core.cs").read_text(encoding="utf-8", errors="replace")
    check("PathSafety" in core, "PathSafety helper present")
    check("RegisterNoLogonSafe" in core, "NoLogon password not on schtasks argv")
    check("/RP" not in core or "RegisterNoLogonSafe" in core, "schtasks /RP avoided for NoLogon")
    check('@"C:\\SQLBackup"' in core or r'"C:\SQLBackup"' in core, "Server default backup path")

    engine = (ROOT / "src/BackupEngine.cs").read_text(encoding="utf-8", errors="replace")
    check("IsUnderRoot" in engine, "bak path traversal guard")
    check("IsSafeDbName" in engine, "db name allowlist")
    check("IsSafeStagingPath" in engine, "staging path allowlist")

    prog = (ROOT / "src/Program.cs").read_text(encoding="utf-8", errors="replace")
    check("--apply-basit" in prog, "headless basit apply switch")
    check("--backup" in prog, "headless backup switch")

    # setup.ps1: no Invoke-Expression
    setup = (ROOT / "setup.ps1").read_text(encoding="utf-8", errors="replace")
    check("Invoke-Expression" not in setup and "iex " not in setup.lower(), "setup.ps1 has no Invoke-Expression")
    server_setup = (ROOT / "setup-server.ps1").read_text(encoding="utf-8", errors="replace")
    check("Invoke-Expression" not in server_setup, "setup-server.ps1 has no Invoke-Expression")

    wf = (ROOT / ".github/workflows/windows-sql-smoke.yml").read_text(encoding="utf-8", errors="replace")
    check("windows-latest" in wf, "workflow uses windows-latest")
    check("localdb" in wf.lower() or "MSSQLLocalDB" in wf, "workflow uses LocalDB (free, no SA secret)")
    # Disallow ${{ secrets.NAME }} custom secrets (LocalDB uses Windows auth; none needed)
    secret_refs = re.findall(r"secrets\.[A-Za-z0-9_]+", wf)
    check(all(s == "secrets.GITHUB_TOKEN" for s in secret_refs), "no custom secrets.* refs (LocalDB Windows auth)")

    # Optional: dist exe presence note (not required for CI build-from-source)
    dist = ROOT / "dist" / "SQLYedeklemeAraci.exe"
    if dist.is_file():
        ok.append(f"dist exe present ({dist.stat().st_size // (1024 * 1024)} MB)")
    else:
        ok.append("dist exe absent (CI will build from source — OK)")

    print("=== validate_package ===")
    for m in ok:
        print("  OK ", m)
    for m in errors:
        print("  FAIL", m)
    print(f"Result: {len(ok)} ok, {len(errors)} fail")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
