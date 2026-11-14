# Deploying the demo to a single VM

TreasuryKit's demo runs on one small Linux VM with Docker Compose: **Caddy** (HTTPS and a password gate), the **Api** (which serves the web UI), the **Worker**, **PostgreSQL** and a nightly **backup**. ADR 0010 explains the choices.

> **Demo only.** The development sign-in is on: anyone who gets past the password gate can act as any tenant and role. Use synthetic data only (for example the files in `tests/TestData/recon`).

## What you need

- A VM with Ubuntu 24.04, 2 GB RAM or more. For example, a Hetzner Cloud **CX22** (about €4–6 a month); any provider works.
- A hostname pointing at the VM, either:
  - your own domain: add an **A record** (e.g. `demo.yourdomain.com`) with the VM's IPv4 address; or
  - a free name derived from the IP: for `203.0.113.5`, use `203-0-113-5.sslip.io`. It needs no setup.
- Ports **80 and 443** reachable from the internet. Caddy needs both to obtain and renew the Let's Encrypt certificate.

## 1. Prepare the server (once)

SSH in as root, or as a sudo user:

```bash
# Docker Engine and the Compose plugin
curl -fsSL https://get.docker.com | sh

# A non-root user for the app, allowed to run Docker
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy

# Firewall: SSH, HTTP, HTTPS only (PostgreSQL is never published)
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443/tcp && ufw --force enable
```

## 2. Copy the deployment files

On your machine, from the repository root:

```bash
ssh deploy@<vm-ip> 'mkdir -p ~/treasurykit'
scp deploy/docker-compose.yml deploy/Caddyfile deploy/backup.sh deploy/.env.example deploy@<vm-ip>:~/treasurykit/
```

## 3. Configure `.env` on the server

```bash
ssh deploy@<vm-ip>
cd ~/treasurykit && cp .env.example .env && chmod 600 .env
openssl rand -hex 32   # run twice: once for POSTGRES_PASSWORD, once for JWT_SIGNING_KEY
docker run --rm caddy:2-alpine caddy hash-password --plaintext 'choose-a-demo-password'
nano .env
```

Set the following in `.env`:
- `SITE_ADDRESS`: your hostname.
- `BASIC_AUTH_USER` and `BASIC_AUTH_HASH`: the hash goes **inside single quotes**, because it contains `$`.
- `POSTGRES_PASSWORD` and `JWT_SIGNING_KEY`: the two generated values.
- `IMAGE_PREFIX`: this is `ghcr.io/<github-owner-in-lower-case>/treasurykit`.

## 4. Allow the server to pull the images

The images are built by GitHub Actions on every push to `main` (workflow **Images and demo deploy**) and published to GitHub Container Registry.

- **If the packages are public:** nothing to do.
- **If they're private** (the default for a private repository): create a GitHub personal access token (classic) with only `read:packages`, then on the server:

  ```bash
  echo '<token>' | docker login ghcr.io -u <github-user> --password-stdin
  ```

## 5. Start

```bash
cd ~/treasurykit
docker compose pull
docker compose up -d
docker compose ps            # all services Up; postgres healthy
docker compose logs -f api   # first start applies database migrations
```

Open `https://<SITE_ADDRESS>`. The browser asks for the demo password, then shows the TreasuryKit sign-in, with a "Demo environment" banner on every page.

The interactive API documentation is at `https://<SITE_ADDRESS>/scalar/v1`, behind the same password. Get a token from `POST /dev/token` and paste it into the **Bearer** field to try requests.

### Loading the demo data

1. Sign in with a new tenant, user `admin` and role **Admin**, and create an account:
   - bank code `058`, account number `0123456789`, currency `NGN`;
   - mapping profile **Generic layout A**.
2. Sign in again as a **Preparer**. Under the account's Upload tab, upload `tests/TestData/recon/expected.csv`, then `tests/TestData/recon/statement.csv`. Reconciliation starts automatically.
3. Set the month to **February 2026**. Use an **Approver** for the Review tab, and the Preparer for Exceptions.

## Updating

- **One click:** run **Actions → Images and demo deploy → Run workflow** with *deploy* ticked. This needs three repository secrets:
  - `DEPLOY_HOST`: the VM's IP or hostname;
  - `DEPLOY_USER`: `deploy`;
  - `DEPLOY_SSH_KEY`: a private key whose public half is in `/home/deploy/.ssh/authorized_keys`.

  It pins `TAG` in `.env` to the commit it built, pulls the images and restarts the services.
- **By hand, on the server:** `docker compose pull && docker compose up -d`.
- **Rollback:** set `TAG=<an earlier commit sha>` in `.env`, then `docker compose up -d`.

If `docker-compose.yml`, `Caddyfile` or `backup.sh` change in the repository, copy them again (step 2) before updating.

## Backups

The `backup` service writes a dump at start-up and then every 24 hours, keeping `BACKUP_KEEP_DAYS` (7) days, in the `backups` volume.

```bash
docker compose exec backup ls -lh /backups

# Copy one off the server
docker compose cp backup:/backups/<file>.dump .

# Restore into the running database: stop the app first, restore, then start it again
docker compose stop api worker
docker compose exec -T backup pg_restore --clean --if-exists -d treasurykit < <file>.dump
docker compose start api worker
```

The backups live on the same VM. For anything you would mind losing, copy them off the machine regularly (e.g. to object storage).

## Useful commands

```bash
docker compose logs -f api worker              # application logs
docker compose logs caddy                      # certificate and proxy logs
docker compose exec postgres psql -U treasurykit   # SQL console
docker compose down                            # stop (volumes, and so the data, are kept)
```

## Troubleshooting

- **No certificate / HTTPS errors:** check that the DNS name resolves to the VM and that ports 80 and 443 are open. `docker compose logs caddy` shows Let's Encrypt's answer.
- **Asked for the password again and again:** the hash in `.env` must be in single quotes.
- **`unauthorized` when pulling images:** see step 4.
- **Worker errors right after the first start:** it starts before the Api has created the tables and retries until they exist (ADR 0002).
