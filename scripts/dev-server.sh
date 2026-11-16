#!/usr/bin/env bash
# Starts a throwaway Jellyfin with the freshly built plugin and a small generated library.
#
#   scripts/dev-server.sh 12.1            # Jellyfin 12.1 on http://localhost:8812
#   scripts/dev-server.sh 10.11.11 8811   # Jellyfin 10.11 on http://localhost:8811
#   scripts/dev-server.sh stop 12.1
#
# Needs Docker and ffmpeg. The dev admin is ponte-dev / ponte-dev (local test server only).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEV="$ROOT/.dev"
ADMIN_USER="ponte-dev"
ADMIN_PASS="ponte-dev"
CLIENT='MediaBrowser Client="ponte-dev", Device="script", DeviceId="ponte-dev-script", Version="1.0"'

if [[ "${1:-}" == "stop" ]]; then
  docker rm -f "ponte-jellyfin-${2:?version}" >/dev/null && echo "stopped"
  exit 0
fi

VERSION="${1:?usage: dev-server.sh <jellyfin-version> [port]}"
case "$VERSION" in
  10.11*) TFM=net9.0; DEFAULT_PORT=8811 ;;
  12*)    TFM=net10.0; DEFAULT_PORT=8812 ;;
  *) echo "unsupported Jellyfin version $VERSION" >&2; exit 1 ;;
esac
PORT="${2:-$DEFAULT_PORT}"
NAME="ponte-jellyfin-$VERSION"
URL="http://localhost:$PORT"
DLL="$ROOT/src/Jellyfin.Plugin.Ponte/bin/Release/$TFM/Jellyfin.Plugin.Ponte.dll"

[[ -f "$DLL" ]] || { echo "build first: dotnet build -c Release (missing $DLL)" >&2; exit 1; }

# ---- Test media ----------------------------------------------------------------------------
MEDIA="$DEV/media"
make_video() { # out size vcodec seconds audio-lang
  [[ -f "$1" ]] && return
  mkdir -p "$(dirname "$1")"
  local v=(-c:v libx264 -preset ultrafast)
  [[ "$3" == hevc ]] && v=(-c:v libx265 -preset ultrafast -pix_fmt yuv420p10le -x265-params log-level=error)
  ffmpeg -loglevel error -y -f lavfi -i "testsrc2=size=$2:rate=24" -f lavfi -i "sine=frequency=440:sample_rate=48000" \
    -t "$4" -map 0:v -map 1:a "${v[@]}" -c:a aac -ac 2 -metadata:s:a:0 language="$5" "$1"
}
make_video "$MEDIA/movies/Big Buck Bunny (2008) [imdbid-tt1254207]/Big Buck Bunny (2008) - 1080p.mkv" 1920x1080 h264 6 por
make_video "$MEDIA/movies/Big Buck Bunny (2008) [imdbid-tt1254207]/Big Buck Bunny (2008) - 2160p.mkv" 3840x2160 hevc 3 eng
printf '1\n00:00:01,000 --> 00:00:04,000\nOlá, Ponte!\n' > "$MEDIA/movies/Big Buck Bunny (2008) [imdbid-tt1254207]/Big Buck Bunny (2008) - 1080p.pt-BR.srt"
make_video "$MEDIA/movies/Ponte Test Clip (2031)/Ponte Test Clip (2031).mp4" 1280x720 h264 4 und
make_video "$MEDIA/shows/Game of Thrones (2011) [imdbid-tt0944947]/Season 01/Game of Thrones S01E01.mkv" 1920x1080 h264 4 eng
make_video "$MEDIA/shows/Game of Thrones (2011) [imdbid-tt0944947]/Season 01/Game of Thrones S01E02.mkv" 1920x1080 h264 4 eng

# ---- Server --------------------------------------------------------------------------------
STATE="$DEV/$VERSION"
PLUGIN_DIR="$STATE/config/plugins/Ponte_0.0.0.1"
rm -rf "$STATE"/config/plugins/Ponte_* "$STATE"/config/data/plugins/Ponte_*
mkdir -p "$PLUGIN_DIR" "$STATE/cache"
cp "$DLL" "$PLUGIN_DIR/"
cat > "$PLUGIN_DIR/meta.json" <<JSON
{"category":"General","changelog":"dev build","description":"Expose your Jellyfin library as a Stremio addon.",
 "guid":"1bbb9efc-a177-428b-acdc-8d9370cc3a62","name":"Ponte","overview":"Your Jellyfin library as a Stremio addon",
 "owner":"franzopl","targetAbi":"10.11.0.0","timestamp":"2026-01-01T00:00:00Z","version":"0.0.0.1","status":"Active",
 "autoUpdate":false,"assemblies":[]}
JSON

docker rm -f "$NAME" >/dev/null 2>&1 || true
docker run -d --name "$NAME" -u "$(id -u):$(id -g)" -p "$PORT:8096" \
  -v "$STATE/config:/config" -v "$STATE/cache:/cache" -v "$MEDIA:/media:ro" \
  "jellyfin/jellyfin:$VERSION" >/dev/null

printf 'waiting for %s ' "$URL"
for _ in $(seq 1 90); do
  curl -sf -m 2 "$URL/System/Info/Public" >/dev/null && curl -sf -m 2 "$URL/Startup/Configuration" -H "Authorization: $CLIENT" >/dev/null 2>&1 && break
  curl -sf -m 2 "$URL/System/Info/Public" 2>/dev/null | grep -q '"StartupWizardCompleted":true' && break
  printf '.'; sleep 2
done
echo

api() { # method path [json] [token]
  local auth="$CLIENT"; [[ -n "${4:-}" ]] && auth="$CLIENT, Token=\"$4\""
  curl -sf -X "$1" "$URL$2" -H "Authorization: $auth" ${3:+-H 'Content-Type: application/json' -d "$3"}
}

if curl -sf "$URL/System/Info/Public" | grep -q '"StartupWizardCompleted":false'; then
  echo "running the startup wizard"
  api POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
  api GET /Startup/User >/dev/null || true
  api POST /Startup/User "{\"Name\":\"$ADMIN_USER\",\"Password\":\"$ADMIN_PASS\"}" >/dev/null
  api POST /Startup/RemoteAccess '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
  api POST /Startup/Complete >/dev/null
fi

TOKEN=$(api POST /Users/AuthenticateByName "{\"Username\":\"$ADMIN_USER\",\"Pw\":\"$ADMIN_PASS\"}" | python3 -c 'import json,sys;print(json.load(sys.stdin)["AccessToken"])')

if ! api GET /Library/VirtualFolders "" "$TOKEN" | grep -q '"Movies"'; then
  echo "adding libraries"
  api POST "/Library/VirtualFolders?name=Movies&collectionType=movies&paths=%2Fmedia%2Fmovies&refreshLibrary=false" '{"LibraryOptions":{}}' "$TOKEN" >/dev/null
  api POST "/Library/VirtualFolders?name=Shows&collectionType=tvshows&paths=%2Fmedia%2Fshows&refreshLibrary=false" '{"LibraryOptions":{}}' "$TOKEN" >/dev/null
fi
api POST /Library/Refresh "" "$TOKEN" >/dev/null

echo "Jellyfin $VERSION with Ponte: $URL  (login $ADMIN_USER / $ADMIN_PASS)"
echo "link page: $URL/ponte/link"
