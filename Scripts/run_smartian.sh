#!/bin/bash
# =============================================================================
# run_smartian.sh
# =============================================================================
# Wrapper script to run Smartian benchmark tests via Docker container.
#
# Usage:
#   ./run_smartian.sh                    # Run full benchmark (resumable)
#   ./run_smartian.sh --single <file>    # Run on single contract
#   ./run_smartian.sh --reset            # Clear checkpoint and restart
#
# Prerequisites:
#   - Docker container 'project2_eval' must be running
#   - Start container with: sudo docker start project2_eval
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Check if Docker container is running
check_docker() {
    if ! docker ps --filter "name=project2_eval" --format "{{.Names}}" | grep -q "project2_eval"; then
        echo "[ERROR] Docker container 'project2_eval' is not running."
        echo ""
        echo "Please start the container first:"
        echo "  sudo docker start project2_eval"
        echo ""
        echo "Or enter the container with:"
        echo "  sudo docker exec -it project2_eval /bin/bash"
        exit 1
    fi
}

# Main
cd "$PROJECT_DIR"

echo "=============================================="
echo "  Smartian Benchmark Runner"
echo "=============================================="
echo ""

# Check Docker
check_docker

# Run Python script
python3 scripts/run_smartian.py "$@"