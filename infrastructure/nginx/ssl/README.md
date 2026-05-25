# Nginx SSL certificates

The proxy expects `cert.pem` and `key.pem` in this directory. These files are git-ignored
and must never be committed.

## Local development
```bash
bash generate-dev-cert.sh
```
This creates a self-signed certificate for `localhost`. Your browser will warn that it is
not trusted — that is expected for a self-signed dev cert.

## Production
Do NOT use the dev certificate in production. Use a real certificate from Let's Encrypt
(certbot) or your CA, then place the resulting `cert.pem` and `key.pem` here (or mount them).
See `docs/DEPLOYMENT.md` for the full Let's Encrypt walkthrough.
