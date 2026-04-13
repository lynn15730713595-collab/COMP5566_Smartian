"""
run_smartian.py
---------------
Run Smartian on T1-T4 benchmark contracts via Docker and collect accuracy + performance metrics.

Features:
  - Real-time progress bar and per-contract metrics
  - Checkpoint/resume: skips already-completed contracts on re-run
  - ASCII summary tables (accuracy + performance) at the end
  - CSV output to results/smartian_results.csv

Usage:
    python scripts/run_smartian.py                    # full benchmark (resumable)
    python scripts/run_smartian.py --single benchmark/T1_Reentrancy/reentrancy_dao.sol
    python scripts/run_smartian.py --reset            # clear checkpoint and start fresh

Prerequisites:
    - Docker container 'project2_eval' must be running
    - sudo password may be required for docker commands
"""

import argparse
import csv
import json
import re
import subprocess
import sys
import time
from pathlib import Path

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

BENCHMARK_DIR = Path("benchmark")
RESULTS_DIR   = Path("results")
CHECKPOINT    = RESULTS_DIR / "smartian_checkpoint.json"
CSV_PATH      = RESULTS_DIR / "smartian_results.csv"

VULN_TYPES = [
    "T1_Reentrancy",
    "T2_Arithmetic",
    "T3_AccessControl",
    "T4_UncheckedCalls",
]

# Smartian bug types that map to each vulnerability category
EXPECTED_BUGS = {
    "T1_Reentrancy":     {"Reentrancy", "Ether Leak", "Block state Dependency"},
    "T2_Arithmetic":     {"Integer Bug", "Integer Overflow", "Integer Underflow", "Assertion Failure"},
    "T3_AccessControl":  {"Transaction Origin Use", "Unprotected Function", "Suicidal", "Assertion Failure"},
    "T4_UncheckedCalls": {"Mishandled Exception", "Unchecked Return Value", "Assertion Failure"},
}

TIMEOUT = 60  # seconds per contract (matching Docker container limit)

CSV_FIELDS = ["tool", "type", "contract", "tp", "fp", "fn",
              "time_s", "executions", "test_cases", "covered_edges",
              "covered_instructions", "found_bugs", "note"]

DOCKER_CONTAINER = "project2_eval"
DOCKER_PASSWORD = "20040112"

# ---------------------------------------------------------------------------
# Docker helpers
# ---------------------------------------------------------------------------

def run_docker_command(cmd: list[str], timeout: int = TIMEOUT) -> dict:
    """Run command inside Docker container, measuring wall-clock time."""
    start = time.time()
    
    # Build the full docker exec command
    full_cmd = ["sudo", "-S", "docker", "exec", DOCKER_CONTAINER] + cmd
    
    try:
        proc = subprocess.Popen(
            full_cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE
        )
        
        # Provide sudo password via stdin
        stdout, stderr = proc.communicate(
            input=f"{DOCKER_PASSWORD}\n".encode(),
            timeout=timeout
        )
        
        elapsed = round(time.time() - start, 2)
        return {
            "time_s": elapsed,
            "timed_out": elapsed >= timeout,
            "returncode": proc.returncode,
            "stdout": stdout.decode(errors="replace"),
            "stderr": stderr.decode(errors="replace"),
        }
    except subprocess.TimeoutExpired:
        proc.kill()
        elapsed = round(time.time() - start, 2)
        return {
            "time_s": elapsed,
            "timed_out": True,
            "returncode": -1,
            "stdout": "",
            "stderr": "Timeout expired",
        }
    except Exception as e:
        elapsed = round(time.time() - start, 2)
        return {
            "time_s": elapsed,
            "timed_out": False,
            "returncode": -1,
            "stdout": "",
            "stderr": str(e),
        }


def copy_to_docker(local_path: Path, docker_path: str) -> bool:
    """Copy a file from local to Docker container."""
    cmd = ["sudo", "-S", "docker", "cp", str(local_path), f"{DOCKER_CONTAINER}:{docker_path}"]
    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE
        )
        stdout, stderr = proc.communicate(input=f"{DOCKER_PASSWORD}\n".encode(), timeout=30)
        return proc.returncode == 0
    except Exception:
        return False


def check_docker_container() -> bool:
    """Check if Docker container is running."""
    cmd = ["sudo", "-S", "docker", "ps", "--filter", f"name={DOCKER_CONTAINER}", "--format", "{{.Names}}"]
    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE
        )
        stdout, _ = proc.communicate(input=f"{DOCKER_PASSWORD}\n".encode(), timeout=10)
        return DOCKER_CONTAINER in stdout.decode()
    except Exception:
        return False


# ---------------------------------------------------------------------------
# Smartian parsing helpers
# ---------------------------------------------------------------------------

def parse_smartian_log(log_content: str) -> dict:
    """Parse Smartian log output to extract metrics."""
    result = {
        "executions": 0,
        "test_cases": 0,
        "covered_edges": 0,
        "covered_instructions": 0,
        "found_bugs": [],
    }
    
    # Parse execution count
    m = re.search(r"executions:\s*(\d+)", log_content, re.IGNORECASE)
    if m:
        result["executions"] = int(m.group(1))
    
    # Parse test cases
    m = re.search(r"test cases:\s*(\d+)", log_content, re.IGNORECASE)
    if m:
        result["test_cases"] = int(m.group(1))
    
    # Parse covered edges
    m = re.search(r"covered edges:\s*(\d+)", log_content, re.IGNORECASE)
    if m:
        result["covered_edges"] = int(m.group(1))
    
    # Parse covered instructions
    m = re.search(r"covered instructions:\s*(\d+)", log_content, re.IGNORECASE)
    if m:
        result["covered_instructions"] = int(m.group(1))
    
    # Parse found bugs - look for bug reports
    bug_patterns = [
        r"Reentrancy",
        r"Ether Leak",
        r"Integer Bug",
        r"Integer Overflow",
        r"Integer Underflow",
        r"Transaction Origin Use",
        r"Mishandled Exception",
        r"Unchecked Return Value",
        r"Block state Dependency",
        r"Assertion Failure",
    ]
    
    for pattern in bug_patterns:
        matches = re.findall(pattern, log_content, re.IGNORECASE)
        if matches:
            # Count occurrences
            count = len(matches)
            bug_name = matches[0]  # Use the first match for the name
            result["found_bugs"].append(f"{bug_name}:{count}")
    
    return result


def parse_smartian_output(log_path: str = "/home/test/output/log.txt") -> dict:
    """Read and parse Smartian output from Docker container."""
    # Read log file from container
    cmd = ["cat", log_path]
    result = run_docker_command(cmd, timeout=10)
    
    if result["returncode"] != 0:
        return {
            "executions": 0,
            "test_cases": 0,
            "covered_edges": 0,
            "covered_instructions": 0,
            "found_bugs": [],
            "raw_log": "",
        }
    
    log_content = result["stdout"]
    parsed = parse_smartian_log(log_content)
    parsed["raw_log"] = log_content
    return parsed


def score_smartian(found_bugs: list[str], expected: set) -> tuple[int, int, int, str]:
    """Score Smartian results against expected vulnerability types.
    
    Returns: (tp, fp, fn, note)
    """
    found_set = set()
    for bug in found_bugs:
        bug_name = bug.split(":")[0]
        found_set.add(bug_name)
    
    # Check if any expected bug was found
    tp_bugs = found_set & expected
    fp_bugs = found_set - expected
    
    tp = 1 if tp_bugs else 0
    fn = 1 - tp
    fp = 1 if fp_bugs else 0
    
    # Generate note
    note_parts = []
    if tp_bugs:
        note_parts.append(f"Detected {', '.join(sorted(tp_bugs))}")
    if fp_bugs:
        if tp_bugs:
            note_parts.append(f"Also found FP: {', '.join(sorted(fp_bugs))}")
        else:
            note_parts.append(f"Found {', '.join(sorted(fp_bugs))} but not expected vulnerability")
    if not found_bugs:
        note_parts.append("No bugs detected")
    
    note = "; ".join(note_parts)
    return tp, fp, fn, note


# ---------------------------------------------------------------------------
# Checkpoint helpers
# ---------------------------------------------------------------------------

def load_checkpoint() -> dict[str, dict]:
    """Load completed results keyed by 'Type/contract.sol'."""
    if CHECKPOINT.exists():
        try:
            return json.loads(CHECKPOINT.read_text(encoding="utf-8"))
        except Exception:
            pass
    return {}


def save_checkpoint(done: dict[str, dict]) -> None:
    RESULTS_DIR.mkdir(exist_ok=True)
    CHECKPOINT.write_text(json.dumps(done, indent=2), encoding="utf-8")


def append_csv_row(row: dict) -> None:
    RESULTS_DIR.mkdir(exist_ok=True)
    write_header = not CSV_PATH.exists()
    with CSV_PATH.open("a", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        if write_header:
            writer.writeheader()
        writer.writerow(row)


# ---------------------------------------------------------------------------
# Progress display
# ---------------------------------------------------------------------------

def progress_bar(current: int, total: int, width: int = 30) -> str:
    filled = int(width * current / total)
    bar = "#" * filled + "-" * (width - filled)
    pct = current / total * 100
    return f"[{bar}] {current}/{total} ({pct:.0f}%)"


def print_contract_header(idx: int, total: int, rel_path: str) -> None:
    print(f"\n{'-'*52}")
    print(f"[{idx}/{total}] {rel_path}")
    print(f"       Progress: {progress_bar(idx, total)}")


def print_contract_result(metrics: dict, found_bugs: list[str], expected: set,
                          tp: int, fp: int, fn: int, note: str) -> None:
    timeout_str = "Yes(!)" if metrics.get("timed_out", False) else "No"
    print(f"  Time:     {metrics.get('time_s', 0)}s")
    print(f"  Timeout:  {timeout_str}")
    print(f"  Executions: {metrics.get('executions', 0)}")
    print(f"  Test Cases: {metrics.get('test_cases', 0)}")
    print(f"  Found:    {found_bugs if found_bugs else '(none)'}")
    print(f"  Expected: {sorted(expected)}")
    print(f"  Result:   TP={tp}  FP={fp}  FN={fn}")
    if note:
        print(f"  Note:     {note}")


# ---------------------------------------------------------------------------
# ASCII table rendering
# ---------------------------------------------------------------------------

def compute_metrics(tp: int, fp: int, fn: int) -> tuple[float, float, float]:
    precision = tp / (tp + fp) if (tp + fp) > 0 else 0.0
    recall    = tp / (tp + fn) if (tp + fn) > 0 else 0.0
    f1 = (2 * precision * recall / (precision + recall)
          if (precision + recall) > 0 else 0.0)
    return precision, recall, f1


def print_accuracy_table(rows: list[dict]) -> None:
    entries, total_tp, total_fp, total_fn = [], 0, 0, 0
    for r in rows:
        tp, fp, fn = r["tp"], r["fp"], r["fn"]
        prec, rec, f1 = compute_metrics(tp, fp, fn)
        entries.append((r["type"], tp, fp, fn, prec, rec, f1))
        total_tp += tp; total_fp += fp; total_fn += fn

    prec_all, rec_all, f1_all = compute_metrics(total_tp, total_fp, total_fn)

    col_type = max(max(len(e[0]) for e in entries), len("Type"), len("Overall"))
    W = [col_type, 4, 4, 4, 11, 8, 6]
    labels = ["Type", "TP", "FP", "FN", "Precision", "Recall", "F1"]

    def fmt_row(cells):
        return "| " + " | ".join(
            str(c).rjust(w - 1) if i > 0 else str(c).ljust(w - 1)
            for i, (c, w) in enumerate(zip(cells, W))
        ) + " |"

    def sep(fill="-"):
        return "+" + "+".join(fill * (w + 2) for w in W) + "+"

    print("\nAccuracy Results (Smartian)")
    print(sep("="))
    print(fmt_row(labels))
    print(sep("="))
    for name, tp, fp, fn, prec, rec, f1 in entries:
        print(fmt_row([name, tp, fp, fn, f"{prec:.3f}", f"{rec:.3f}", f"{f1:.2f}"]))
    print(sep("-"))
    print(fmt_row(["Overall", total_tp, total_fp, total_fn,
                   f"{prec_all:.3f}", f"{rec_all:.3f}", f"{f1_all:.2f}"]))
    print(sep("="))


def print_performance_table(rows: list[dict]) -> None:
    entries = []
    for r in rows:
        times, execs, tos = r["times"], r["executions"], r["timeouts"]
        avg_t   = sum(times) / len(times) if times else 0.0
        avg_e   = sum(execs) / len(execs) if execs else 0.0
        to_rate = sum(tos) / len(tos) * 100 if tos else 0.0
        entries.append((r["type"], avg_t, avg_e, to_rate))

    col_type = max(max(len(e[0]) for e in entries), len("Type"))
    W = [col_type, 10, 14, 14]
    labels = ["Type", "Avg Time", "Avg Executions", "Timeout Rate"]

    def fmt_row(cells):
        return "| " + " | ".join(
            str(c).rjust(w - 1) if i > 0 else str(c).ljust(w - 1)
            for i, (c, w) in enumerate(zip(cells, W))
        ) + " |"

    def sep(fill="-"):
        return "+" + "+".join(fill * (w + 2) for w in W) + "+"

    print("\nPerformance Results (Smartian)")
    print(sep("="))
    print(fmt_row(labels))
    print(sep("="))
    for name, avg_t, avg_e, to_rate in entries:
        print(fmt_row([name, f"{avg_t:.2f}s", f"{avg_e:.0f}", f"{to_rate:.0f}%"]))
    print(sep("="))


# ---------------------------------------------------------------------------
# Single-contract analysis
# ---------------------------------------------------------------------------

def run_smartian_in_docker(sol_path: Path, timeout: int = TIMEOUT) -> dict:
    """Run Smartian on a single contract inside Docker container.
    
    This function:
    1. Copies the contract to the Docker container
    2. Runs Smartian via the container's run_smartian.sh script
    3. Parses the output log
    """
    contract_name = sol_path.stem
    type_name = sol_path.parent.name
    
    # Docker paths
    docker_sol_path = f"/home/test/temp_contracts/{contract_name}.sol"
    docker_bin_path = f"/home/test/temp_contracts/{contract_name}.bin"
    docker_abi_path = f"/home/test/temp_contracts/{contract_name}.abi"
    
    # Ensure temp directory exists
    run_docker_command(["mkdir", "-p", "/home/test/temp_contracts"], timeout=5)
    
    # Copy contract to container
    if not copy_to_docker(sol_path, docker_sol_path):
        return {
            "type": type_name,
            "contract": sol_path.name,
            "tp": 0, "fp": 0, "fn": 1,
            "time_s": 0,
            "executions": 0,
            "test_cases": 0,
            "covered_edges": 0,
            "covered_instructions": 0,
            "found_bugs": "",
            "note": "Failed to copy contract to Docker container",
            "_found": [],
            "_expected": EXPECTED_BUGS.get(type_name, set()),
            "_metrics": {"timed_out": False},
        }
    
    # Run Smartian using the container's script
    # The container has run_smartian.sh which takes: timeout dummy.sol bin_file abi_file name ""
    start = time.time()
    
    # First compile the contract to get bin and abi
    compile_cmd = [
        "bash", "-c",
        f"cd /home/test && solc --bin --abi {docker_sol_path} -o /home/test/temp_contracts --overwrite 2>/dev/null || true"
    ]
    run_docker_command(compile_cmd, timeout=30)
    
    # Check if bin/abi files were created
    check_bin = run_docker_command(["ls", f"/home/test/temp_contracts/{contract_name}.bin"], timeout=5)
    
    if check_bin["returncode"] != 0:
        # Try alternative naming (contract name might differ from file name)
        # Find any .bin file in temp_contracts
        find_bin = run_docker_command(["bash", "-c", "ls /home/test/temp_contracts/*.bin 2>/dev/null | head -1"], timeout=5)
        if find_bin["stdout"].strip():
            docker_bin_path = find_bin["stdout"].strip()
            docker_abi_path = docker_bin_path.replace(".bin", ".abi")
        else:
            return {
                "type": type_name,
                "contract": sol_path.name,
                "tp": 0, "fp": 0, "fn": 1,
                "time_s": round(time.time() - start, 2),
                "executions": 0,
                "test_cases": 0,
                "covered_edges": 0,
                "covered_instructions": 0,
                "found_bugs": "",
                "note": "Failed to compile contract",
                "_found": [],
                "_expected": EXPECTED_BUGS.get(type_name, set()),
                "_metrics": {"timed_out": False},
            }
    
    # Run Smartian
    smartian_cmd = [
        "bash", "-c",
        f"cd /home/test/scripts && ./run_smartian.sh {timeout} dummy.sol {docker_bin_path} {docker_abi_path} {contract_name} ''"
    ]
    
    result = run_docker_command(smartian_cmd, timeout=timeout + 10)
    elapsed = round(time.time() - start, 2)
    
    # Parse the output log
    parsed = parse_smartian_output()
    
    # Score the results
    expected = EXPECTED_BUGS.get(type_name, set())
    tp, fp, fn, note = score_smartian(parsed["found_bugs"], expected)
    
    return {
        "type": type_name,
        "contract": sol_path.name,
        "tp": tp, "fp": fp, "fn": fn,
        "time_s": elapsed,
        "executions": parsed["executions"],
        "test_cases": parsed["test_cases"],
        "covered_edges": parsed["covered_edges"],
        "covered_instructions": parsed["covered_instructions"],
        "found_bugs": ";".join(parsed["found_bugs"]),
        "note": note,
        "_found": parsed["found_bugs"],
        "_expected": expected,
        "_metrics": {"timed_out": result["timed_out"]},
    }


# ---------------------------------------------------------------------------
# Alternative: Use pre-existing B1 benchmarks in Docker
# ---------------------------------------------------------------------------

def run_smartian_on_b1_benchmark(contract_name: str, type_name: str, timeout: int = TIMEOUT) -> dict:
    """Run Smartian on a contract from the B1 benchmark inside Docker.
    
    The Docker container has pre-compiled contracts in /home/test/benchmarks/B1/
    """
    start = time.time()
    
    # Run Smartian using the container's script
    smartian_cmd = [
        "bash", "-c",
        f"cd /home/test/scripts && ./run_smartian.sh {timeout} dummy.sol "
        f"/home/test/benchmarks/B1/bin/{contract_name}.bin "
        f"/home/test/benchmarks/B1/abi/{contract_name}.abi "
        f"{contract_name} ''"
    ]
    
    result = run_docker_command(smartian_cmd, timeout=timeout + 10)
    elapsed = round(time.time() - start, 2)
    
    # Parse the output log
    parsed = parse_smartian_output()
    
    # Score the results
    expected = EXPECTED_BUGS.get(type_name, set())
    tp, fp, fn, note = score_smartian(parsed["found_bugs"], expected)
    
    return {
        "type": type_name,
        "contract": f"{contract_name}.sol",
        "tp": tp, "fp": fp, "fn": fn,
        "time_s": elapsed,
        "executions": parsed["executions"],
        "test_cases": parsed["test_cases"],
        "covered_edges": parsed["covered_edges"],
        "covered_instructions": parsed["covered_instructions"],
        "found_bugs": ";".join(parsed["found_bugs"]),
        "note": note,
        "_found": parsed["found_bugs"],
        "_expected": expected,
        "_metrics": {"timed_out": result["timed_out"]},
    }


# ---------------------------------------------------------------------------
# Main benchmark runner
# ---------------------------------------------------------------------------

def collect_contracts() -> list[Path]:
    contracts = []
    for vtype in VULN_TYPES:
        d = BENCHMARK_DIR / vtype
        if not d.exists():
            print(f"[WARN] Directory not found: {d}", file=sys.stderr)
            continue
        sols = [s for s in sorted(d.glob("*.sol"))
                if not s.name.endswith(("_exp.sol", "_vuln.sol"))]
        contracts.extend(sols)
    return contracts


def run_full_benchmark(reset: bool = False) -> None:
    # Check Docker container
    if not check_docker_container():
        print(f"[ERROR] Docker container '{DOCKER_CONTAINER}' is not running.", file=sys.stderr)
        print("Please start the container first:", file=sys.stderr)
        print(f"  sudo docker start {DOCKER_CONTAINER}", file=sys.stderr)
        sys.exit(1)
    
    contracts = collect_contracts()
    if not contracts:
        print("No contracts found. Check BENCHMARK_DIR path.", file=sys.stderr)
        sys.exit(1)

    total = len(contracts)

    # ── Checkpoint ────────────────────────────────────────────────────────
    if reset:
        if CHECKPOINT.exists():
            CHECKPOINT.unlink()
        if CSV_PATH.exists():
            CSV_PATH.unlink()
        print("[reset] Checkpoint and CSV cleared.\n")

    done = load_checkpoint()
    skipped = len(done)
    remaining = total - skipped

    print(f"Smartian benchmark -- {total} contracts across {len(VULN_TYPES)} types")
    if skipped:
        print(f"[resume] {skipped} already done, {remaining} remaining")
    print(f"Timeout per contract: {TIMEOUT}s")
    print(f"Docker container: {DOCKER_CONTAINER}\n")

    # ── Per-type accumulators (seed from checkpoint) ───────────────────────
    acc_agg: dict[str, dict] = {t: {"tp": 0, "fp": 0, "fn": 0} for t in VULN_TYPES}
    perf_agg: dict[str, dict] = {t: {"times": [], "executions": [], "timeouts": []}
                                  for t in VULN_TYPES}

    for key, r in done.items():
        vt = r["type"]
        if vt in acc_agg:
            acc_agg[vt]["tp"] += r["tp"]
            acc_agg[vt]["fp"] += r["fp"]
            acc_agg[vt]["fn"] += r["fn"]
        if vt in perf_agg:
            perf_agg[vt]["times"].append(r["time_s"])
            perf_agg[vt]["executions"].append(r["executions"])
            perf_agg[vt]["timeouts"].append(int(r.get("timed_out", False)))

    # ── Run ───────────────────────────────────────────────────────────────
    display_idx = 0
    for seq_idx, sol_path in enumerate(contracts, start=1):
        key = f"{sol_path.parent.name}/{sol_path.name}"

        if key in done:
            r = done[key]
            print(f"[skip] {key}  (TP={r['tp']} FP={r['fp']} FN={r['fn']})")
            continue

        display_idx += 1
        print_contract_header(seq_idx, total, key)

        result = run_smartian_in_docker(sol_path)
        tp, fp, fn = result["tp"], result["fp"], result["fn"]
        print_contract_result(
            result["_metrics"], result["_found"], result["_expected"], tp, fp, fn, result["note"]
        )

        # Accumulate
        vt = result["type"]
        if vt in acc_agg:
            acc_agg[vt]["tp"] += tp
            acc_agg[vt]["fp"] += fp
            acc_agg[vt]["fn"] += fn
        if vt in perf_agg:
            perf_agg[vt]["times"].append(result["time_s"])
            perf_agg[vt]["executions"].append(result["executions"])
            perf_agg[vt]["timeouts"].append(int(result["_metrics"].get("timed_out", False)))

        # Checkpoint + CSV (after every contract)
        done[key] = {
            "type":            result["type"],
            "contract":        result["contract"],
            "tp":              tp, "fp": fp, "fn": fn,
            "time_s":          result["time_s"],
            "executions":      result["executions"],
            "test_cases":      result["test_cases"],
            "covered_edges":   result["covered_edges"],
            "covered_instructions": result["covered_instructions"],
            "found_bugs":      result["found_bugs"],
            "note":            result["note"],
        }
        save_checkpoint(done)
        append_csv_row({"tool": "smartian", **done[key]})

    # ── Summary ───────────────────────────────────────────────────────────
    print("\n" + "=" * 52)
    acc_rows  = [{"type": t, **acc_agg[t]}  for t in VULN_TYPES]
    perf_rows = [{"type": t, **perf_agg[t]} for t in VULN_TYPES if perf_agg[t]["times"]]
    print_accuracy_table(acc_rows)
    if perf_rows:
        print_performance_table(perf_rows)
    print(f"\nResults saved to {CSV_PATH}")
    print(f"Checkpoint:       {CHECKPOINT}")


def run_single_mode(sol_path: Path) -> None:
    if not sol_path.exists():
        print(f"File not found: {sol_path}", file=sys.stderr)
        sys.exit(1)

    # Check Docker container
    if not check_docker_container():
        print(f"[ERROR] Docker container '{DOCKER_CONTAINER}' is not running.", file=sys.stderr)
        sys.exit(1)

    print(f"Smartian -- single file: {sol_path}")
    print_contract_header(1, 1, str(sol_path))

    result = run_smartian_in_docker(sol_path)
    tp, fp, fn = result["tp"], result["fp"], result["fn"]
    print_contract_result(
        result["_metrics"], result["_found"], result["_expected"], tp, fp, fn, result["note"]
    )
    print_accuracy_table([{"type": result["type"], "tp": tp, "fp": fp, "fn": fn}])
    print_performance_table([{
        "type":       result["type"],
        "times":      [result["time_s"]],
        "executions": [result["executions"]],
        "timeouts":   [int(result["_metrics"].get("timed_out", False))],
    }])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Run Smartian on smart contract benchmark (T1-T4) via Docker."
    )
    parser.add_argument("--single", metavar="FILE",
                        help="Analyse a single .sol file instead of the full benchmark.")
    parser.add_argument("--benchmark-dir", default="benchmark",
                        help="Path to benchmark directory (default: benchmark)")
    parser.add_argument("--reset", action="store_true",
                        help="Clear checkpoint and CSV, restart from scratch.")
    args = parser.parse_args()

    BENCHMARK_DIR = Path(args.benchmark_dir)

    if args.single:
        run_single_mode(Path(args.single))
    else:
        run_full_benchmark(reset=args.reset)