#!/bin/bash
# Flowamaz Community Edition installer — get.flowamaz.io/community
# Usage: curl -fsSL https://get.flowamaz.io/community/install.sh | bash
set -euo pipefail

echo "Installing Flowamaz Community Edition..."

# Prerequisites.
command -v docker >/dev/null 2>&1 || { echo "Docker is required. Install it from https://docker.com and re-run."; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is required (the 'docker compose' command)."; exit 1; }

INSTALL_DIR="${FLOWAMAZ_HOME:-$HOME/.flowamaz}"
mkdir -p "$INSTALL_DIR"

# Download the compose file.
echo "Downloading docker-compose.yml..."
curl -fsSL https://get.flowamaz.io/community/docker-compose.yml -o "$INSTALL_DIR/docker-compose.yml"

# Generate secrets on first install (never ship hardcoded defaults).
if [ ! -f "$INSTALL_DIR/.env" ]; then
  echo "Generating secure configuration..."
  JWT_SECRET=$(openssl rand -hex 32)
  CREDENTIAL_MASTER_KEY=$(openssl rand -hex 32)
  GATE_SIGNING_KEY=$(openssl rand -hex 32)
  POSTGRES_PASSWORD=$(openssl rand -hex 16)
  cat > "$INSTALL_DIR/.env" <<EOF
JWT_SECRET=$JWT_SECRET
CREDENTIAL_MASTER_KEY=$CREDENTIAL_MASTER_KEY
GATE_SIGNING_KEY=$GATE_SIGNING_KEY
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
EOF
  chmod 600 "$INSTALL_DIR/.env"
  echo "Configuration saved to $INSTALL_DIR/.env"
else
  echo "Reusing existing configuration at $INSTALL_DIR/.env"
fi

# Start the stack.
docker compose -f "$INSTALL_DIR/docker-compose.yml" --env-file "$INSTALL_DIR/.env" up -d

echo ""
echo "✓ Flowamaz Community Edition is running!"
echo "  Open:    http://localhost:3000"
echo "  Logs:    docker compose -f $INSTALL_DIR/docker-compose.yml logs -f"
echo "  Stop:    docker compose -f $INSTALL_DIR/docker-compose.yml down"
