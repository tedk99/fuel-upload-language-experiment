#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY_HOST="${DEPLOY_HOST:-legion}"
DEPLOY_DIR="${DEPLOY_DIR:-/home/ted/Develop/experiment}"
SSH_PORT="${SSH_PORT:-22}"
BOOK_STATIC_BIND="${BOOK_STATIC_BIND:-0.0.0.0}"
BOOK_STATIC_PORT="${BOOK_STATIC_PORT:-8898}"
BOOK_JUPYTER_BIND="${BOOK_JUPYTER_BIND:-127.0.0.1}"
BOOK_JUPYTER_PORT="${BOOK_JUPYTER_PORT:-8899}"

log() {
  printf '[book-deploy] %s\n' "$*" >&2
}

remote_quote() {
  printf "%q" "$1"
}

cleanup() {
  if [[ -n "${ARCHIVE:-}" && -f "$ARCHIVE" ]]; then
    rm -f "$ARCHIVE"
  fi
}
trap cleanup EXIT

cd "$ROOT"

log "Rendering static reader before packaging."
python3 tools/render_static_book.py

ARCHIVE="$(mktemp -t fuel-book-deploy.XXXXXX.tar.gz)"
log "Packing repo, notebooks, and book/container files into $ARCHIVE."
COPYFILE_DISABLE=1 tar -czf "$ARCHIVE" \
  --exclude='.git' \
  --exclude='.claude' \
  --exclude='.quarto' \
  --exclude='.quarto-cli' \
  --exclude='.venv-jupyter-book' \
  --exclude='._*' \
  --exclude='.__*' \
  --exclude='**/__pycache__' \
  --exclude='**/bin' \
  --exclude='**/obj' \
  --exclude='**/dist' \
  --exclude='**/dist-newstyle' \
  --exclude='**/node_modules' \
  --exclude='**/target' \
  --exclude='**/.spago' \
  --exclude='**/output' \
  .

log "Copying package to $DEPLOY_HOST:$DEPLOY_DIR."
ssh -p "$SSH_PORT" "$DEPLOY_HOST" "mkdir -p '$DEPLOY_DIR'"
scp -P "$SSH_PORT" "$ARCHIVE" "$DEPLOY_HOST:/tmp/fuel-book-deploy.tar.gz"

log "Building and starting containers on $DEPLOY_HOST."
REMOTE_DEPLOY_DIR="$(remote_quote "$DEPLOY_DIR")"
REMOTE_STATIC_BIND="$(remote_quote "$BOOK_STATIC_BIND")"
REMOTE_STATIC_PORT="$(remote_quote "$BOOK_STATIC_PORT")"
REMOTE_JUPYTER_BIND="$(remote_quote "$BOOK_JUPYTER_BIND")"
REMOTE_JUPYTER_PORT="$(remote_quote "$BOOK_JUPYTER_PORT")"

ssh -p "$SSH_PORT" "$DEPLOY_HOST" "bash -s" <<EOF
set -Eeuo pipefail
cd $REMOTE_DEPLOY_DIR
tar -xzf /tmp/fuel-book-deploy.tar.gz
rm -f /tmp/fuel-book-deploy.tar.gz

BOOK_STATIC_BIND=$REMOTE_STATIC_BIND \\
BOOK_STATIC_PORT=$REMOTE_STATIC_PORT \\
BOOK_JUPYTER_BIND=$REMOTE_JUPYTER_BIND \\
BOOK_JUPYTER_PORT=$REMOTE_JUPYTER_PORT \\
  docker compose -f docker-compose.book.yml up -d --build

docker compose -f docker-compose.book.yml ps

for attempt in \$(seq 1 20); do
  if curl -fsS "http://127.0.0.1:$REMOTE_STATIC_PORT/" >/dev/null; then
    exit 0
  fi
  sleep 1
done

docker logs fuel-book-static --tail 80 >&2 || true
docker logs fuel-book-jupyter --tail 80 >&2 || true
exit 1
EOF

log "Static reader for everyone: http://$DEPLOY_HOST:$BOOK_STATIC_PORT/"
log "JupyterLab has the deployed repo/notebooks and is bound on $BOOK_JUPYTER_BIND:$BOOK_JUPYTER_PORT; use docker logs fuel-book-jupyter for the tokenized URL."
