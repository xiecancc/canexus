#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""README 类型引用一致性校验（CI 门禁，backend-ci.yml 调用）。

扫描 README.md 中反引号包裹、形如 C# 类型名的标识符，逐个在
canexus-api-netcore/src/**/*.cs 中核验该类型确实被声明
（class / interface / record / struct / enum <Name>）；泛型写法 Foo<T> 与
CLR 泛型元数 Foo`1 均归一化为简单名 Foo 后比对。

用法（ubuntu-latest 自带 python3）：
    python3 scripts/check-readme-types.py

退出码：0 = 全部通过并打印统计；1 = README 引用了仓库中不存在的类型，
或白名单项 / 排除集项失实。
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

# 以脚本自身位置定位项目根，CI 的 working-directory 变化不影响结果。
PROJECT_ROOT = Path(__file__).resolve().parent.parent
README = PROJECT_ROOT / "README.md"
SRC_DIR = PROJECT_ROOT / "src"
TEST_DIR = PROJECT_ROOT / "test"

# ---------------------------------------------------------------------------
# 白名单：确实是「概念名 / 外部包类型」而非本仓库声明的条目。
#
# 准入规则（脚本强制核验）：除「概念名」外，每个白名单项必须在
# src/**/*.cs 中以 using / 调用形式真实出现过（整词命中），防止把拼错的
# 类型名塞进白名单蒙混过关。
# ---------------------------------------------------------------------------
EXTERNAL_TYPE_WHITELIST = {
    # README 中已引用的外部包 / BCL 类型
    "IRequest": "MediatR 请求标记接口（外部包）",
    "RequestHandlerDelegate": "MediatR 管道委托（外部包）",
    "CancellationToken": "System.Threading 取消令牌（BCL）",
    "ConfigurationManager": "Microsoft.Extensions.Configuration（BCL）",
    "SugarTable": "SqlSugar 表映射特性（外部包）",
    "SugarColumn": "SqlSugar 列映射特性（外部包）",
    "ExcelColumnName": "MiniExcel 列映射特性（外部包）",
    # 预留：以下外部包类型同样已在 src 中确认出现，便于后续 README 引用
    "IJob": "Quartz 作业接口（外部包）",
    "HybridCache": "HybridCache 双层缓存（外部包）",
    "SqlSugarScope": "SqlSugar 客户端作用域（外部包）",
    "IDistributedLock": "分布式锁抽象（外部包）",
    "MiniExcel": "MiniExcel 入口类型（外部包）",
}

# 概念名：README 以否定句明确声明本项目「不定义 / 不使用」，故 src 中刻意不出现，
# 豁免上面的「必须在 src 中出现」核验（否则无法表达「项目没有 IQuery」这一事实）。
CONCEPT_ONLY_WHITELIST = {
    "IQuery": "CQRS 读查询标记接口概念：README 明确声明本项目未定义、不使用",
}

# ---------------------------------------------------------------------------
# 非类型 token 排除集：反引号包裹但不是「类型引用」的内联代码
# （方法 / 属性 / 字段 / 关键字 / MSBuild 属性 / 环境名 / 列名）。
# 这些按形状不属于 C# 类型声明，跳过核验、不计入检查统计；
# 若其中某项日后在仓库中被声明为类型，脚本会报错要求移出本集合。
# ---------------------------------------------------------------------------
NON_TYPE_TOKENS = {
    # 成员 / 方法名
    "Version", "ExecuteCommandWithOptLock", "RestoreAsync", "RunInTransactionAsync",
    "ThrowIfCancellationRequested", "Send", "ActionArguments", "ContentType", "PasswordHash",
    "HardDeleteAsync", "SoftDeleteAsync", "UpdateAsync", "DeletedAt", "Scripts", "ApplyAsync",
    # 构建属性（.csproj）
    "TreatWarningsAsErrors", "EnforceCodeStyleInBuild",
    # 环境名 / 数据库列名
    "Testing", "MigrationId",
}

# 内联代码 → 类型简单名：仅接受纯 PascalCase 标识符，可带 <...> 泛型实参。
SPAN_RE = re.compile(r"^([A-Z][A-Za-z0-9]*)(?:<[^`]*>)?$")

# C# 类型声明：class / interface / record / struct / enum（含常见修饰符）。
DECL_RE = re.compile(
    r"^\s*(?:public|internal|protected|private|file)?\s*"
    r"(?:sealed\s+|abstract\s+|partial\s+|static\s+|readonly\s+)*"
    r"(?:class|interface|record|struct|enum)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)",
    re.M,
)


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def strip_generic_arity(name: str) -> str:
    """去掉 CLR 泛型元数后缀（`1 / `2 ...），返回可比对的类型简单名。"""
    return re.sub(r"`\d+$", "", name)


def extract_type_name(raw: str) -> str | None:
    """把内联反引号代码归一化为待核验的类型简单名；非类型名形状返回 None。"""
    m = SPAN_RE.match(raw.strip())
    if not m:
        return None
    return strip_generic_arity(m.group(1))


def collect_sources():
    """一次读取全部 .cs：主校验源 src，补充 test（测试基础设施类型，如 ApiTestFactory）。"""
    src_files = sorted(SRC_DIR.rglob("*.cs"))
    test_files = sorted(TEST_DIR.rglob("*.cs")) if TEST_DIR.is_dir() else []
    declared: set[str] = set()
    src_parts: list[str] = []
    for path in src_files:
        text = read_text(path)
        src_parts.append(text)
        for m in DECL_RE.finditer(text):
            declared.add(strip_generic_arity(m.group(1)))
    for path in test_files:
        for m in DECL_RE.finditer(read_text(path)):
            declared.add(strip_generic_arity(m.group(1)))
    return declared, len(src_files) + len(test_files), "\n".join(src_parts)


def collect_readme_candidates():
    """返回 {类型简单名: (原始 token, [行号...])}；跳过围栏代码块（目录树 / bash 示例）。"""
    occurrences: dict[str, tuple[str, list[int]]] = {}
    fence = False
    for lineno, line in enumerate(read_text(README).splitlines(), 1):
        if line.lstrip().startswith("```"):
            fence = not fence
            continue
        if fence:
            continue
        for raw in re.findall(r"`([^`\n]+)`", line):
            name = extract_type_name(raw)
            if name is None:
                continue
            if name in occurrences:
                occurrences[name][1].append(lineno)
            else:
                occurrences[name] = (raw.strip(), [lineno])
    return occurrences


def main() -> int:
    if not README.is_file():
        print(f"错误：找不到 README：{README}", file=sys.stderr)
        return 1
    if not SRC_DIR.is_dir():
        print(f"错误：找不到源码目录：{SRC_DIR}", file=sys.stderr)
        return 1

    declared, file_count, src_blob = collect_sources()
    candidates = collect_readme_candidates()
    errors: list[str] = []

    # 1) 白名单准入：外部包类型必须确实在 src 中以完整标识符出现过（概念名豁免）。
    for name, reason in EXTERNAL_TYPE_WHITELIST.items():
        if not re.search(r"\b" + re.escape(name) + r"\b", src_blob):
            errors.append(
                f"白名单项 `{name}`（{reason}）未在 src/**/*.cs 中出现，"
                f"不满足准入规则，请移除或更正命名"
            )

    # 2) 排除集有效性：其中不得出现已在仓库中声明的类型（否则应走正常类型核验）。
    for name in sorted(NON_TYPE_TOKENS):
        if name in declared:
            errors.append(
                f"非类型排除集项 `{name}` 现已在仓库中声明为类型，"
                f"请从 NON_TYPE_TOKENS 移除并按类型引用核验"
            )

    whitelist = set(EXTERNAL_TYPE_WHITELIST) | set(CONCEPT_ONLY_WHITELIST)
    ok_declared = 0
    ok_whitelist = 0
    skipped = 0
    missing: list[str] = []

    for name, (raw, lines) in sorted(candidates.items()):
        if name in NON_TYPE_TOKENS:
            skipped += 1
        elif name in declared:
            ok_declared += 1
        elif name in whitelist:
            ok_whitelist += 1
        else:
            loc = ", ".join(f"README.md:{n}" for n in sorted(set(lines)))
            missing.append(f"  - `{raw}` -> 类型简单名 `{name}`（{loc}）")

    # 3) README 引用了本仓库不存在的类型：清晰列表 + 非零退出。
    if missing:
        errors.append("README 引用了本仓库不存在的类型：\n" + "\n".join(missing))

    if errors:
        print("README 类型引用校验失败：", file=sys.stderr)
        for err in errors:
            print("  " + err, file=sys.stderr)
        print(
            "\n处理：修正 README 中的类型名；若确为外部包类型，先确认其在 src 中"
            "以 using/调用形式出现，再加入 EXTERNAL_TYPE_WHITELIST 并注明来源。",
            file=sys.stderr,
        )
        return 1

    checked = ok_declared + ok_whitelist
    print(
        f"README 类型引用校验通过：共检查 {checked} 个类型引用"
        f"（{ok_declared} 个在 {file_count} 个 .cs 文件中声明、"
        f"{ok_whitelist} 个为白名单外部/概念名）；"
        f"跳过 {skipped} 个非类型内联 token。"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
