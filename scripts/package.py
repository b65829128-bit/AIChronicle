#!/usr/bin/env python3
"""编译 + 部署 + 打包纯净发布版（Linux/macOS/Windows 通用，Python 标准库）。

产物：dist/AIChronicle_v<版本>.zip
解压后把 AIChronicle 文件夹整个放进游戏 Modules/ 即可。

用法：
  python3 scripts/package.py              # 编译 + 部署 + 打包
  python3 scripts/package.py --skip-build # 跳过编译，仅打包（需已编译）
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODULE = "AIChronicle"


def build() -> None:
    print("[1/3] 编译 + 部署 ...", flush=True)
    subprocess.run(
        ["dotnet", "build", str(ROOT / "AIChronicle.csproj"), "-c", "Release", "-f", "net472"],
        cwd=ROOT,
        check=True,
    )


def read_version() -> str:
    xml = (ROOT / "_Module" / "SubModule.xml").read_text(encoding="utf-8")
    match = re.search(r'<Version\s+value="v?([^"]+)"', xml)
    if not match:
        raise SystemExit("无法从 _Module/SubModule.xml 读取版本号")
    return match.group(1)


def assemble() -> Path:
    print("[2/3] 组装纯净包 ...", flush=True)
    dist = ROOT / "dist"
    staging = dist / MODULE
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True)

    shutil.copy2(ROOT / "_Module" / "SubModule.xml", staging / "SubModule.xml")
    shutil.copytree(ROOT / "_Module" / "GUI", staging / "GUI")

    campaigns = ROOT / "_Module" / "Prompts" / "Campaigns"
    if campaigns.exists():
        shutil.copytree(
            ROOT / "_Module" / "Prompts",
            staging / "Prompts",
            ignore=shutil.ignore_patterns("Campaigns"),
        )
    else:
        shutil.copytree(ROOT / "_Module" / "Prompts", staging / "Prompts")

    out_dir = ROOT / "bin" / "Release" / "net472"
    if not (out_dir / f"{MODULE}.dll").is_file():
        raise SystemExit(f"未找到编译产物 {out_dir / f'{MODULE}.dll'}，请先编译")

    bin_dir = staging / "bin" / "Win64_Shipping_Client"
    bin_dir.mkdir(parents=True)
    dlls = sorted(out_dir.glob("*.dll"))
    for dll in dlls:
        shutil.copy2(dll, bin_dir / dll.name)
    print(f"      运行时 DLL: {len(dlls)} 个（含 NAudio 依赖）", flush=True)

    shutil.copy2(ROOT / "README_MOD.md", staging / "README_MOD.md")
    shutil.copy2(ROOT / "LICENSE", staging / "LICENSE")
    install_doc = next(
        (p for p in ROOT.glob("*.txt") if "安装" in p.name or "readme" in p.name.lower()),
        None,
    )
    if install_doc:
        shutil.copy2(install_doc, staging / install_doc.name)

    return staging


def make_zip(staging: Path, version: str) -> Path:
    print("[3/3] 压缩 ...", flush=True)
    zip_path = ROOT / "dist" / f"AIChronicle_v{version}.zip"
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(staging.rglob("*")):
            if path.is_file():
                zf.write(path, Path(MODULE) / path.relative_to(staging))
    return zip_path


def main() -> None:
    parser = argparse.ArgumentParser(description="打包 AI Chronicle 发布版")
    parser.add_argument("--skip-build", action="store_true", help="跳过编译，仅打包")
    args = parser.parse_args()

    if not args.skip_build:
        build()
    else:
        print("[1/3] 跳过编译（--skip-build）", flush=True)

    version = read_version()
    staging = assemble()
    zip_path = make_zip(staging, version)
    shutil.rmtree(staging)

    print(f"完成：{zip_path}（{zip_path.stat().st_size / 1024:.1f} KB）", flush=True)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as exc:
        sys.exit(f"编译失败（退出码 {exc.returncode}）")
