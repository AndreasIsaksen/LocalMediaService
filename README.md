# LocalMediaService

A local media server web application that brings common streaming platforms (Netflix, Prime Video, and more) into one start page and also plays videos stored on local storage.

## Features

- Single web UI with quick links to popular media platforms.
- Local video library discovery from a mounted media folder.
- In-browser playback for common video formats (`.mp4`, `.mkv`, `.webm`, `.mov`, `.avi`).
- Docker-ready deployment for home server environments.

## Run locally

```bash
dotnet run --project /home/runner/work/LocalMediaService/LocalMediaService/LocalMediaService.Web/LocalMediaService.Web.csproj
```

Open `http://localhost:5119` (or the URL printed by ASP.NET Core).

To use a custom media folder:

```bash
MEDIA_ROOT=/absolute/path/to/your/videos dotnet run --project /home/runner/work/LocalMediaService/LocalMediaService/LocalMediaService.Web/LocalMediaService.Web.csproj
```

## Run with Docker Compose

1. Put local video files in `/home/runner/work/LocalMediaService/LocalMediaService/media` (or change the volume mapping in `docker-compose.yml`).
2. Start:

```bash
docker compose up --build -d
```

3. Open `http://localhost:8080`.

The container mounts `./media` as read-only to `/media` and uses that directory for local playback.
