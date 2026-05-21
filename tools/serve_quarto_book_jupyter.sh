#!/usr/bin/env bash
set -Eeuo pipefail

# Render the checked-in book reader and serve the repo through JupyterLab.
# The served default URL is the static book page, so readers do not need Quarto
# or language kernels installed.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV_DIR="${JUPYTER_BOOK_VENV:-$ROOT/.venv-jupyter-book}"
HOST="${HOST:-127.0.0.1}"
PORT="${PORT:-8888}"
OPEN_BROWSER="${OPEN_BROWSER:-0}"

usage() {
  cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --host HOST       Bind address for JupyterLab. Default: $HOST
  --port PORT       Port for JupyterLab. Default: $PORT
  --static          Accepted for compatibility; the static reader is always rendered
  --open-browser    Let Jupyter try to open a browser
  -h, --help        Show this help

Environment:
  JUPYTER_BOOK_VENV=...   Override the local Jupyter venv path
  OPEN_BROWSER=0          Set to 1 to let Jupyter open a browser
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
    --static)
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

log "Rendering dependency-free static reader."
python3 "$ROOT/tools/render_static_book.py"

if [[ ! -f "$ROOT/docs/book/index.html" ]]; then
  printf '[book-jupyter] ERROR: docs/book/index.html was not created\n' >&2
  exit 1
fi

BROWSER_FLAG=(--no-browser)
if [[ "$OPEN_BROWSER" == "1" ]]; then
  BROWSER_FLAG=()
fi

log "Serving docs/book/index.html through JupyterLab."
exec "$VENV_DIR/bin/python" -m jupyter lab \
  --ServerApp.root_dir="$ROOT" \
  --ServerApp.default_url="/files/docs/book/index.html" \
  --ip="$HOST" \
  --port="$PORT" \
  --allow-root \
  "${BROWSER_FLAG[@]}"
