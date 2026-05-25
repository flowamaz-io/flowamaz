#!/bin/bash
# Generates a self-signed dev certificate for localhost. NEVER use in production —
# replace with a real cert (see docs/DEPLOYMENT.md, Let's Encrypt).
set -euo pipefail
cd "$(dirname "$0")"

openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout key.pem -out cert.pem \
  -subj "/C=MY/ST=Kuala Lumpur/L=KL/O=Flowamaz/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

echo "Dev SSL certificate generated: cert.pem + key.pem"
