#!/usr/bin/env python3
"""Mac/Linux/Windows: package structure + Aze-like config sanity (no SQL Server required)."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
ok: list[str] = []


def _safe_print(*args: object) -> None:
    """Windows GHA consoles are often cp1252 — avoid UnicodeEncodeError on Turkish chars."""
    try:
        print(*args)
    except UnicodeEncodeError:
        text = " ".join(str(a) for a in args)
        print(text.encode("ascii", "replace").decode("ascii"))


def check(cond: bool, msg: str) -> None:
    if cond:
        ok.append(msg)
    else:
        errors.append(msg)


def main() -> int:
    required = [
        "README.md",
        "HIZLI_BASLANGIC.md",
        "basit-ayarlar.json",
        "client_secret.json.ornek",
        "backup.master.sql",
        "setup.bat",
        "setup.ps1",
        "src/Program.cs",
        "src/Core.cs",
        "src/BackupEngine.cs",
        "src/AzeSimpleForm.cs",
        "src/DriveApiClient.cs",
        "src/DriveOAuth.cs",
        "src/StartupHelper.cs",
        "src/MailNotifier.cs",
        "src/SQLYedeklemeAraci.csproj",
        ".github/workflows/windows-sql-smoke.yml",
    ]
    for rel in required:
        check((ROOT / rel).is_file(), f"exists: {rel}")

    simple = json.loads((ROOT / "basit-ayarlar.json").read_text(encoding="utf-8"))
    for key in ("DriveMail", "DriveSifre", "Saat", "SqlSunucu", "SqlKullanici", "SqlSifre", "SaklamaAdedi"):
        check(key in simple, f"basit-ayarlar has Aze field {key}")
    check(simple.get("Hedef") in ("GoogleDrive", "GoogleDriveApi", "Yerel"), "Hedef valid")
    check(simple.get("Hedef") == "GoogleDriveApi", "default Hedef GoogleDriveApi (headless Aze path)")
    check(isinstance(simple.get("SaklamaAdedi"), int) and 1 <= simple["SaklamaAdedi"] <= 365, "SaklamaAdedi range")
    check(bool(re.fullmatch(r"\d{1,2}:\d{2}", str(simple.get("Saat", "")))), "Saat HH:mm")
    check(not str(simple.get("DriveSifre") or "").strip(), "sample DriveSifre empty")

    sql = (ROOT / "backup.master.sql").read_text(encoding="utf-8", errors="replace")
    for token in ("BACKUP DATABASE", "RESTORE VERIFYONLY", "QUOTENAME"):
        check(token in sql, f"SQL contains {token}")

    core = (ROOT / "src/Core.cs").read_text(encoding="utf-8", errors="replace")
    for token in ("DriveMail", "DriveAppPasswordEnc", "DriveRefreshTokenEnc", "ApplyAzeMailFromDrive",
                  "GoogleDriveApi", "SetDriveRefreshToken", "StartWithWindows"):
        check(token in core, f"Core has {token}")

    aze = (ROOT / "src/AzeSimpleForm.cs").read_text(encoding="utf-8", errors="replace")
    for token in ("Google e-posta", "uygulama şifresi", "SQL sunucu", "Test Et", "Ayarları Kaydet",
                  "Windows açılınca", "Google Drive'ı Bağla"):
        check(token in aze, f"Aze UI has '{token}'")

    oauth = (ROOT / "src/DriveOAuth.cs").read_text(encoding="utf-8", errors="replace")
    check("AuthorizeInteractiveAsync" in oauth and "RefreshToken" in oauth, "OAuth refresh token flow")
    check("Logger" not in oauth or "RefreshToken" in oauth, "OAuth module present")

    drive = (ROOT / "src/DriveApiClient.cs").read_text(encoding="utf-8", errors="replace")
    check("HasDriveUserOAuth" in drive or "FromRefreshToken" in drive, "Drive client supports user OAuth")
    check("UploadZip" in drive and "EnforceRetention" in drive, "upload + retention")

    engine = (ROOT / "src/BackupEngine.cs").read_text(encoding="utf-8", errors="replace")
    check("RequiresDriveApiUpload" in engine, "engine uploads when API dest")
    check("MailNotifier.NotifyBackup" in engine, "mail after backup")
    check("BACKUP DATABASE" in sql, "SQL remains source")

    startup = (ROOT / "src/StartupHelper.cs").read_text(encoding="utf-8", errors="replace")
    check("--tray" in startup and "CurrentVersion\\Run" in startup, "startup Run key + tray")

    prog = (ROOT / "src/Program.cs").read_text(encoding="utf-8", errors="replace")
    check("DriveMail" in prog and "SqlSunucu" in prog, "ApplyBasit reads Aze fields")
    check("StartupHelper.Apply" in prog, "apply enables Windows startup")

    csproj = (ROOT / "src/SQLYedeklemeAraci.csproj").read_text(encoding="utf-8")
    check("Google.Apis.Drive.v3" in csproj and "Google.Apis.Auth" in csproj, "Drive NuGet refs")

    hizli = (ROOT / "HIZLI_BASLANGIC.md").read_text(encoding="utf-8", errors="replace")
    check("Aze" in hizli and "uygulama şifresi" in hizli and "Google Drive'ı Bağla" in hizli, "hızlı başlangıç Aze checklist")

    smoke = (ROOT / "tests/windows_sql_smoke.ps1").read_text(encoding="utf-8", errors="replace")
    check("Yerel" in smoke, "GHA smoke stays Yerel (no Drive secrets)")

    wf = (ROOT / ".github/workflows/windows-sql-smoke.yml").read_text(encoding="utf-8", errors="replace")
    check("windows-2022" in wf or "windows-latest" in wf, "windows-2022 (or latest) runner")
    secret_refs = re.findall(r"secrets\.[A-Za-z0-9_]+", wf)
    check(all(s == "secrets.GITHUB_TOKEN" for s in secret_refs), "no custom GHA secrets")

    dist = ROOT / "dist" / "SQLYedeklemeAraci.exe"
    ok.append(f"dist exe {'present' if dist.is_file() else 'absent (CI builds)'} ")

    _safe_print("=== validate_package ===")
    for m in ok:
        _safe_print("  OK ", m)
    for m in errors:
        _safe_print("  FAIL", m)
    _safe_print(f"Result: {len(ok)} ok, {len(errors)} fail")
    return 1 if errors else 0


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # type: ignore[attr-defined]
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")  # type: ignore[attr-defined]
    except Exception:
        pass
    sys.exit(main())
