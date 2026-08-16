# LocalMediaService

LocalMediaService is a private, containerized home-media portal. It provides one authenticated start page for commercial streaming services, an encrypted account vault, and direct playback of video files from a read-only external drive with sidecar SRT subtitles.

The deployment uses host port **8092** by default. A read-only audit of `aragnaroth@10.230.1.208` on 2026-08-16 found that EatMe already publishes **8080** and **8081**, while **8092** was unused. No changes were made to that server during the audit.

## What is included

- Responsive portal for Netflix, Prime Video, Disney+, HBO Max, Apple TV+, NRK TV, TV 2 Play, Viaplay, and YouTube.
- Configurable service list in `LocalMediaService.Web/appsettings.json`.
- Local administrator login with an `HttpOnly`, same-site authentication cookie.
- AES-256-GCM encrypted storage for provider usernames and passwords.
- Masked account summaries and administrator-password confirmation before revealing a credential.
- Cached, recursive discovery of local video files using opaque media IDs.
- Byte-range responses for seeking in supported browser media.
- Automatic discovery and conversion of `.srt` sidecars to WebVTT for the HTML player.
- Caddy TLS gateway with a persistent local certificate authority for encrypted LAN traffic.
- Read-only external-drive mount and persistent Docker volumes for encrypted portal data and TLS state.
- Health checks, CSRF protection, login rate limiting, security headers, and path/symlink hardening.
- Non-root, read-only application and gateway containers with no Linux capabilities and an isolated Docker network.
- Automated tests for the media catalog, subtitles, encryption, configuration, and authentication verifier.

## Architecture

```text
LAN browser ── HTTPS :8092 ──> Caddy TLS gateway ── private HTTP ──> portal :8080
       │                         │                                      │       │
       │                         └─ local CA in gateway-data             │       └─ portal-data
       │                                                                │          encrypted credentials
       │                                                                │          + cookie keys
       │                                                                └─ /media (read-only bind mount)
       │                                                                   videos + sidecar SRT files
       │
       └─ official provider site in a new tab (Netflix, Prime Video, etc.)
          provider owns authentication, cookies, MFA, DRM, and playback
```

The portal's internal port `8080` does not conflict with EatMe; Docker isolates container ports. It is not published on the host. Only Caddy publishes host port `8092`, and both containers use their own `local-media-service-network`.

## Important streaming-service limitation

Commercial providers do not permit their sites or protected video streams to be placed in a general-purpose local HTML player. Their pages use cross-origin isolation, anti-framing policies, provider cookies, MFA, and DRM. Consequently:

- the portal opens each provider's official HTTPS site in a new tab;
- it does not embed, proxy, scrape, or bypass a provider player;
- it cannot automatically fill a saved password into another origin;
- the browser/provider session remains the normal way to stay signed in.

The vault is an optional convenience for credentials you choose to store. A browser password manager remains the safer choice for most households.

## Server prerequisites

- Linux host with Docker Engine and Docker Compose v2.
- An external filesystem mounted at a stable host path, for example `/mnt/local-media`.
- Media directories and files readable by the container's non-root user.
- A stable hostname or IP address that client devices use for the portal's local TLS certificate.
- TCP port `8092` available to the trusted LAN.

The audited home server is compatible: Ubuntu 24.04 amd64, Docker 29.7.2, and Compose 5.4.0. It has a low-power Celeron N3450 and 3.7 GiB RAM, which is suitable for direct play but not heavy software transcoding. No external media filesystem was mounted when it was inspected, so that host-side mount must be prepared before deployment.

### 1. Prepare the external drive

Mount the drive outside this repository using its filesystem UUID and a persistent `/etc/fstab` entry. Confirm the mount before continuing:

```bash
findmnt --mountpoint /mnt/local-media
# Alternatively: mountpoint -q /mnt/local-media
```

After the actual drive is mounted, create the sentinel file on the drive:

```bash
sudo touch /mnt/local-media/.local-media-volume
```

The health check requires that sentinel. This prevents an unmounted but otherwise empty `/mnt/local-media` directory from being treated as the media disk. The Compose bind also uses `create_host_path: false`, so a path that does not exist fails instead of being silently created.

Grant read and directory-traverse permission only as broadly as appropriate for the host. The current .NET runtime image uses UID/GID `1654` for its `app` user; verify it after building with:

```bash
docker run --rm local-media-service:latest id
```

The application never needs write access to the media drive.

### 2. Use a predictable media layout

The folder names are flexible, but this convention keeps videos and subtitles easy to maintain:

```text
/mnt/local-media/
├── .local-media-volume
├── Movies/
│   └── Arrival (2016)/
│       ├── Arrival (2016).mp4
│       ├── Arrival (2016).en.srt
│       └── Arrival (2016).nb.srt
└── TV Shows/
    └── Example Show/
        └── Season 01/
            ├── Example Show - S01E01.mp4
            └── Example Show - S01E01.no.srt
```

A subtitle must share the video's complete basename. Supported patterns are `Film.srt`, `Film.en.srt`, `Film.nb.srt`, and similar language suffixes. SRT timestamps are converted to WebVTT as the track is served; source files are never modified.

### 3. Configure secrets and paths

Copy the template:

```bash
cp .env.example .env
```

Generate a unique credential-encryption key and a strong portal password:

```bash
openssl rand -base64 32
openssl rand -base64 36
```

Edit `.env` and place the generated values in `LMS_CREDENTIAL_KEY` and `LMS_ADMIN_PASSWORD`. Also set `MEDIA_PATH` to the real mounted drive. `.env` is ignored by Git.

Do not change `LMS_CREDENTIAL_KEY` after saving accounts. Existing records cannot be decrypted without the original key.

### 4. Start the stack

Set `LOCAL_MEDIA_HOST` to the exact hostname or IP address clients will use. Caddy creates a local certificate for this value and persists its certificate authority in `local-media-service-gateway-data`.

```bash
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

Open:

```text
https://10.230.1.208:8092
```

Before entering credentials, trust the local Caddy certificate authority on each client device. Export its public root certificate after the first start:

```bash
docker compose cp gateway:/data/caddy/pki/authorities/local/root.crt ./local-media-service-ca.crt
```

On Debian/Ubuntu, install it with:

```bash
sudo install -m 0644 ./local-media-service-ca.crt \
  /usr/local/share/ca-certificates/local-media-service.crt
sudo update-ca-certificates
```

Other operating systems, phones, tablets, and TV devices have their own trusted-certificate workflow. An untrusted-certificate warning is expected until this one-time step is complete; do not enter provider credentials through a warning page. Sign in with `LMS_ADMIN_USERNAME` (default `admin`) and the password from `.env` only after the certificate is trusted.

The server's firewall state could not be checked without interactive administrator access. If a firewall is active, allow TCP `8092` only from the trusted LAN. Do not expose this portal directly to the public Internet.

## Configuration

| Variable | Default | Purpose |
|---|---:|---|
| `MEDIA_PATH` | required | Host path of the mounted external media drive |
| `LOCAL_MEDIA_BIND` | `0.0.0.0` | Host interface on which the TLS gateway listens; use a LAN IP to narrow exposure |
| `LOCAL_MEDIA_PORT` | `8092` | Published host port; `8080` and `8081` are reserved by EatMe |
| `LOCAL_MEDIA_HOST` | required | Exact hostname or IP used by clients and placed in the local TLS certificate |
| `LMS_ADMIN_USERNAME` | `admin` | Portal administrator username |
| `LMS_ADMIN_PASSWORD` | required | Portal administrator password, minimum 12 characters |
| `LMS_CREDENTIAL_KEY` | required | Base64-encoded 32-byte AES key |
| `TZ` | `Europe/Oslo` | Container timezone |

Application settings use normal ASP.NET Core configuration. For example, `MediaLibrary__ScanIntervalSeconds=60` changes the media-index cache interval. The mount sentinel defaults to `.local-media-volume` through `MediaLibrary:MountSentinelFile`.

The gateway always listens on unprivileged port `8092` inside its container, so `LOCAL_MEDIA_PORT=443` is also valid if standard HTTPS is preferred on the host and that port is free.

## Credential-vault security

The provider username and password are serialized together and encrypted with AES-256-GCM using a random nonce and authenticated record metadata. The JSON file in `/data` contains ciphertext, not those plaintext values. The encryption key is supplied separately through `.env`; authentication-cookie keys live alongside the ciphertext in the persistent data volume.

This protects a copied data volume from casual disclosure when the `.env` key is kept separately. It does **not** protect against a Docker/host administrator, a compromised running container, screen capture while a secret is revealed, or an attacker who obtains both the data volume and `.env`. Use a unique portal password and store only credentials you accept having on this server.

TLS is mandatory in the Compose deployment: only Caddy is host-published, secure cookies are required, forwarded scheme headers are trusted only on the isolated proxy network, and direct HTTP requests to sensitive portal routes receive `426 Upgrade Required`. State-changing requests require an antiforgery token. Login attempts are limited to five per source IP per minute, and reveal attempts to three per account/IP window every five minutes. Changing the administrator password invalidates existing authentication tickets. Credentials are never returned in service-list responses and reveal responses are marked non-cacheable.

## Media compatibility

The catalog recognizes `.mp4`, `.m4v`, `.webm`, `.ogv`, `.mov`, `.mkv`, and `.avi`. Recognition does not guarantee that a browser supports the file's container, video codec, and audio codec.

- MP4 with H.264 video and AAC audio is the safest choice across browsers and TVs.
- WebM is broadly supported in modern browsers.
- MKV, AVI, MOV, H.265/HEVC, and some audio formats are browser/device-dependent and are labelled accordingly in the UI.
- This service performs direct play and SRT conversion; it does not transcode video.

For a library that requires automatic transcoding, use Jellyfin or a dedicated FFmpeg/HLS backend. The audited server exposes `/dev/dri/renderD128`, so a future Jellyfin deployment should use Intel hardware acceleration; software transcoding on that CPU is not recommended. Jellyfin would still not combine commercial provider streams into its local player.

## Operations

Check status and health:

```bash
docker compose ps
curl --fail --cacert ./local-media-service-ca.crt https://10.230.1.208:8092/health/live
curl --fail --cacert ./local-media-service-ca.crt https://10.230.1.208:8092/health/ready
```

Follow logs:

```bash
docker compose logs -f --tail=100 portal
```

After adding media, use **Rescan** in the UI. The next normal request also refreshes an index older than 30 seconds.

Update the application:

```bash
git pull --ff-only
docker compose up --build -d
```

Stop without deleting saved accounts:

```bash
docker compose down
```

Do not use `docker compose down -v` unless you intentionally want to delete the credential store, cookie keys, and local TLS certificate authority.

### Backup

Back up all three of these as one set:

1. the `local-media-service-data` Docker volume;
2. the `local-media-service-gateway-data` volume, which contains the trusted local CA and its private key;
3. the `LMS_CREDENTIAL_KEY` value from `.env`, stored separately and securely.

For example, from the repository root:

```bash
mkdir -p backups
docker run --rm \
  --mount source=local-media-service-data,target=/source,readonly \
  --mount type=bind,source="$PWD/backups",target=/backup \
  alpine:3.23 \
  tar -C /source -czf /backup/local-media-service-data.tar.gz .

docker run --rm \
  --mount source=local-media-service-gateway-data,target=/source,readonly \
  --mount type=bind,source="$PWD/backups",target=/backup \
  alpine:3.23 \
  tar -C /source -czf /backup/local-media-service-gateway-data.tar.gz .
```

Treat the gateway-data backup as sensitive because its CA private key can issue certificates trusted by your clients. The external media drive should have its own backup strategy; it is not copied into the portal data volume.

## Local development and tests

.NET 10 SDK is required. Create development folders and the sentinel, then supply non-production secrets:

```bash
mkdir -p media data
touch media/.local-media-volume
export MediaLibrary__RootPath="$PWD/media"
export Storage__DataPath="$PWD/data"
export PortalSecurity__AdminPassword='replace-with-a-long-development-password'
export PortalSecurity__RequireHttps=false
export CredentialStore__EncryptionKey="$(openssl rand -base64 32)"
dotnet run --project LocalMediaService.Web
```

The launch profile listens on `http://localhost:5092`.

Run all automated tests:

```bash
dotnet test LocalMediaService.slnx -c Release
```

Build and validate Compose:

```bash
dotnet build LocalMediaService.slnx -c Release
docker compose config --quiet
docker compose build
```

## Project structure

```text
LocalMediaService.Web/
├── Endpoints/       authenticated HTTP endpoints and health checks
├── Models/          API and encrypted-storage records
├── Options/         validated application configuration
├── Services/        media indexing, SRT conversion, auth, and encryption
├── wwwroot/         login page, portal UI, CSS, and browser JavaScript
├── Dockerfile       multi-stage, non-root .NET image
└── Program.cs       application composition and security middleware
LocalMediaService.Gateway/
├── Caddyfile         local HTTPS and private reverse proxy
└── Dockerfile        pinned, non-root Caddy image
LocalMediaService.Tests/
└── focused unit and filesystem security tests
docker-compose.yml   hardened two-container deployment
.env.example         deployment configuration template
```

## Adding or changing services

Edit `StreamingServices:Services` in `LocalMediaService.Web/appsettings.json` and rebuild the image. Each item needs:

- a unique lowercase `Id` containing letters, numbers, or hyphens;
- a display `Name`;
- official HTTPS `HomeUrl` and `LoginUrl` values;
- optional descriptive text and an accent colour.

Only trusted configuration should be used. Service URLs are validated as HTTPS at startup.

## Troubleshooting

- **Portal is unhealthy:** confirm `findmnt --mountpoint "$MEDIA_PATH"`, verify `.local-media-volume` is on the mounted drive, and check `docker compose logs portal`.
- **Gateway is unhealthy:** check `docker compose logs gateway`, verify `LOCAL_MEDIA_HOST`, and confirm port `8092` is free.
- **Browser reports an untrusted certificate:** export the Caddy root certificate and install it on that client. Preserve `local-media-service-gateway-data` so the CA does not change.
- **Compose says a required variable is missing:** create `.env` from `.env.example` and replace all secret placeholders.
- **Library is empty:** verify read/traverse permissions, the sentinel file, supported extensions, and then press **Rescan**.
- **Video loads but does not play:** the browser does not support that container/codec combination; remux/transcode to MP4/H.264/AAC or use a transcoding server.
- **Subtitles are missing:** put the `.srt` beside the video and make the complete video basename match before the language suffix.
- **Saved accounts can no longer decrypt:** restore the original `LMS_CREDENTIAL_KEY` together with the matching data-volume backup.
- **Portal is unreachable from another device:** confirm the configured `LOCAL_MEDIA_HOST` resolves to the server, port `8092` is listening, and the host firewall allows it only from the trusted LAN.
