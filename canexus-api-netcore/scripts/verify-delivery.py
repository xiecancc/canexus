#!/usr/bin/env python3
"""交付门禁一键校验（P2-5）。

把 README「任务完成后的固定动作」里可自动验证的部分固化为脚本：

    python scripts/verify-delivery.py                # 本地：提交前（build + 校验 + 全量测试）
    python scripts/verify-delivery.py --post-push   # 本地：推送后追加（工作区干净 + refs 一致）
    python scripts/verify-delivery.py --ci          # CI：跳过工作区 / refs 检查（detached HEAD 无远端跟踪）

约定：
- 集成测试依赖 SUPER_ADMIN_TEST_CONN 与本地 MySQL / Redis；脚本不负责注入，
  由调用方（本机环境变量或 workflow env）提供，缺失时 dotnet test 会如实失败。
- 只做校验，不改文件、不提交、不推送；格式化请显式运行 dotnet format（不带 --verify）。
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# 脚本位于 <repo>/canexus-api-netcore/scripts/，解决方案在该目录上一层
BACKEND_DIR = Path(__file__).resolve().parent.parent
REPO_DIR = BACKEND_DIR.parent
SOLUTION = "api.slnx"
README_CHECK = "scripts/check-readme-types.py"


def run(label: str, command: list[str], cwd: Path, stream: bool = False) -> bool:
    """执行一条校验命令，打印结果；CI 模式下实时透传输出便于排查。"""
    print(f"[verify] {label} ...", flush=True)
    result = subprocess.run(
        command,
        cwd=cwd,
        capture_output=not stream,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode == 0:
        print(f"[verify] PASS  {label}")
        return True

    print(f"[verify] FAIL  {label} (exit={result.returncode})")
    if not stream:
        output = (result.stdout or "") + (result.stderr or "")
        tail = "\n".join(output.strip().splitlines()[-15:])
        if tail:
            print(tail)
    return False


def git_output(args: list[str]) -> str:
    """在仓库根执行 git 命令并返回标准输出。"""
    result = subprocess.run(
        ["git", *args],
        cwd=REPO_DIR,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return result.stdout.strip()


def check_clean_worktree() -> bool:
    """收尾校验：工作区必须干净（pre-push 钩子生成的噪音需先还原）。"""
    dirty = git_output(["status", "--porcelain"])
    if dirty:
        print("[verify] 工作区不干净：")
        print(dirty)
        return False
    print("[verify] PASS  工作区干净")
    return True


def print_coverage_summary(min_rate: float) -> bool:
    """汇总 TestResults 下的 cobertura 报告，打印行覆盖率；指定阈值时作为门禁。"""
    results = BACKEND_DIR / "TestResults"
    files = sorted(results.rglob("coverage.cobertura.xml"))
    if not files:
        print("[verify] WARN  未找到 cobertura 覆盖率报告（可能测试未运行或采集失败）")
        return min_rate <= 0

    valid = covered = 0
    for report in files:
        try:
            root = ET.parse(report).getroot()
            valid += int(root.attrib.get("lines-valid", 0))
            covered += int(root.attrib.get("lines-covered", 0))
        except (ET.ParseError, OSError, ValueError) as ex:
            print(f"[verify] WARN  无法解析覆盖率报告 {report}: {ex}")
    if valid == 0:
        print("[verify] WARN  覆盖率报告无有效行数据")
        return min_rate <= 0

    rate = covered / valid * 100
    print(f"[verify] 行覆盖率：{rate:.2f}%（{covered}/{valid} 行，{len(files)} 份报告）")
    if min_rate > 0 and rate < min_rate:
        print(f"[verify] FAIL  行覆盖率低于门禁阈值 {min_rate:.2f}%")
        return False
    if min_rate > 0:
        print(f"[verify] PASS  行覆盖率满足门禁阈值 {min_rate:.2f}%")
    return True


def check_refs() -> bool:
    """收尾校验：local / gitee refs 必须一致；github 可达时一并核对。"""
    head = git_output(["rev-parse", "HEAD"])
    gitee = git_output(["rev-parse", "gitee/master"])
    github = git_output(["rev-parse", "github/master"])

    ok = True
    if not gitee:
        print("[verify] SKIP  gitee/master 不存在（首次推送或远端未抓取）")
    elif gitee != head:
        print(f"[verify] FAIL  gitee/master={gitee} 与 HEAD={head} 不一致")
        ok = False
    else:
        print("[verify] PASS  local / gitee refs 一致")

    # GitHub 推送失败时按项目约定仅推 gitee，故此处不一致只提示、不判失败
    if github and github != head:
        print(f"[verify] WARN  github/master={github} 与 HEAD 不一致（按约定允许仅推 gitee）")
    elif github:
        print("[verify] PASS  local / gitee / github refs 一致")
    return ok


def main() -> int:
    parser = argparse.ArgumentParser(description="canexus 交付门禁校验")
    parser.add_argument("--post-push", action="store_true", help="追加工作区干净与 refs 一致校验")
    parser.add_argument("--ci", action="store_true", help="CI 模式：跳过工作区 / refs 检查")
    parser.add_argument("--skip-tests", action="store_true", help="跳过全量测试（仅调试脚本本身时用）")
    parser.add_argument("--no-restore", action="store_true", help="复用已 restore 的产物，CI Restore 步骤之后使用")
    parser.add_argument("--coverage", action="store_true", help="测试时采集 XPlat Code Coverage 并打印行覆盖率摘要（P2-3）")
    parser.add_argument("--min-line-rate", type=float, default=0.0,
                        help="行覆盖率最低百分比（>0 时启用硬门禁；0 表示只采集与报告）")
    args = parser.parse_args()

    restore_flag = ["--no-restore"] if args.no_restore else []
    test_command = ["dotnet", "test", SOLUTION, "--no-build", "--verbosity", "quiet", "-nologo"]
    if args.coverage:
        test_command += [
            "--collect:XPlat Code Coverage",
            "--results-directory", "TestResults",
            "--DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura",
        ]
    checks: list[tuple[str, list[str], Path]] = [
        ("README 类型引用校验", [sys.executable, README_CHECK], BACKEND_DIR),
        ("解决方案构建", ["dotnet", "build", SOLUTION, *restore_flag, "--verbosity", "quiet", "-nologo"], BACKEND_DIR),
        ("格式校验 (dotnet format --verify-no-changes)",
         ["dotnet", "format", SOLUTION, *restore_flag, "--verify-no-changes", "--verbosity", "quiet"], BACKEND_DIR),
    ]
    if not args.skip_tests:
        checks.append(("全量测试", test_command, BACKEND_DIR))

    failed = False
    for label, command, cwd in checks:
        if not run(label, command, cwd, stream=args.ci):
            failed = True
            # 前置失败继续执行后续项，便于一次拿到全部问题；但测试依赖构建产物
            if label.startswith("解决方案构建"):
                print("[verify] 构建失败，后续测试大概率无意义，提前结束")
                break

    if not args.ci and args.post_push and not failed:
        if not check_clean_worktree():
            failed = True
        if not check_refs():
            failed = True

    if args.coverage and not failed and not print_coverage_summary(args.min_line_rate):
        failed = True

    print("[verify] 结果：" + ("FAILED" if failed else "ALL PASS"))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
