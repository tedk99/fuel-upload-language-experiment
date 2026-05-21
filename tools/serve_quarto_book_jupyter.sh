#!/usr/bin/env bash
set -Eeuo pipefail

# Serve the book as JupyterLab notebooks.
#
# Each chapter .qmd is converted to a real .ipynb under docs/book-notebooks/
# (prose -> markdown cells, code -> language-tagged code cells, mermaid -> inline
# pre-rendered SVG). JupyterLab opens the notebook tree, so readers get
# cell-by-cell navigation instead of a wall of static HTML.
#
# No language kernels are wired up — code cells are intentionally read-only
# studyware. The pre-rendered SVG diagrams come from Quarto's HTML build, which
# this script will trigger if Quarto is on PATH and the notebooks need refresh.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV_DIR="${JUPYTER_BOOK_VENV:-$ROOT/.venv-jupyter-book}"
HOST="${HOST:-0.0.0.0}"
PORT="${PORT:-8888}"
OPEN_BROWSER="${OPEN_BROWSER:-0}"
SKIP_BUILD="${SKIP_BUILD:-0}"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --host HOST       Bind address for JupyterLab. Default: $HOST
  --port PORT       Port for JupyterLab. Default: $PORT
  --skip-build      Skip the notebook rebuild step; serve whatever is checked in
  --open-browser    Let Jupyter try to open a browser
  -h, --help        Show this help

Environment:
  JUPYTER_BOOK_VENV=...   Override the local Jupyter venv path
  OPEN_BROWSER=0          Set to 1 to let Jupyter open a browser
  SKIP_BUILD=0            Set to 1 to skip the notebook rebuild
EOF
}

log() {
  printf '[book-jupyter] %s\n' "$*" >&2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)
      HOST="${2:?missing host}"
      shift 2
      ;;
    --port)
      PORT="${2:?missing port}"
      shift 2
      ;;
    --skip-build)
      SKIP_BUILD=1
      shift
      ;;
    --static)
      # Legacy flag from the old static-HTML serve script. Accepted for
      # compatibility (docker-compose passes it) but is now a no-op.
      shift
      ;;
    --open-browser)
      OPEN_BROWSER=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf '[book-jupyter] ERROR: unknown option: %s\n' "$1" >&2
      exit 1
      ;;
  esac
done

if [[ ! -x "$VENV_DIR/bin/python" ]]; then
  log "Creating Jupyter virtual environment at $VENV_DIR"
  python3 -m venv "$VENV_DIR"
fi

if ! "$VENV_DIR/bin/python" -m jupyter lab --version >/dev/null 2>&1; then
  log "Installing JupyterLab into the local virtual environment."
  "$VENV_DIR/bin/python" -m pip install --upgrade pip
  "$VENV_DIR/bin/python" -m pip install jupyterlab
fi

if [[ "$SKIP_BUILD" != "1" ]]; then
  log "Building chapter notebooks (with pre-rendered SVG diagrams)."
  python3 "$ROOT/tools/build_book_notebooks.py"
else
  log "SKIP_BUILD=1 — serving the checked-in notebooks as-is."
fi

INDEX="$ROOT/docs/book-notebooks/index.ipynb"
if [[ ! -f "$INDEX" ]]; then
  printf '[book-jupyter] ERROR: %s does not exist; run tools/build_book_notebooks.py\n' "$INDEX" >&2
  exit 1
fi

BROWSER_FLAG=(--no-browser)
if [[ "$OPEN_BROWSER" == "1" ]]; then
  BROWSER_FLAG=()
fi

log "Serving JupyterLab; landing on docs/book-notebooks/index.ipynb"
exec "$VENV_DIR/bin/python" -m jupyter lab \
  --ServerApp.root_dir="$ROOT" \
  --ServerApp.default_url="/lab/tree/docs/book-notebooks/index.ipynb" \
  --ip="$HOST" \
  --port="$PORT" \
  --allow-root \
  "${BROWSER_FLAG[@]}"
