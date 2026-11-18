#!/usr/bin/env python3
"""Walks a dev server's addon the way a Stremio client does and checks every resource.

    scripts/smoke_test.py http://localhost:8812

Expects the library scripts/dev-server.sh creates. Exits non-zero on the first failed check.
"""

import json
import sys
import time
import urllib.error
import urllib.request

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8812").rstrip("/")
CLIENT = 'MediaBrowser Client="ponte-dev", Device="script", DeviceId="ponte-dev-script", Version="1.0"'
failures = 0


def request(path, method="GET", body=None, token=None, headers=None, raw=False):
    url = path if path.startswith("http") else BASE + path
    h = {"Authorization": CLIENT + (f', Token="{token}"' if token else "")}
    h.update(headers or {})
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        h["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    with urllib.request.urlopen(req, timeout=30) as r:
        payload = r.read()
        return (r.status, dict(r.headers), payload) if raw else json.loads(payload or b"null")


def check(name, ok, detail=""):
    global failures
    print(("  ok   " if ok else "  FAIL ") + name + (f"  ({detail})" if detail else ""))
    if not ok:
        failures += 1


auth = request("/Users/AuthenticateByName", "POST", {"Username": "ponte-dev", "Pw": "ponte-dev"})
token = auth["AccessToken"]

# Wait for the library scan to find the test titles.
for _ in range(60):
    movies_found = request("/Items?Recursive=true&IncludeItemTypes=Movie", token=token).get("TotalRecordCount", 0)
    episodes_found = request("/Items?Recursive=true&IncludeItemTypes=Episode", token=token).get("TotalRecordCount", 0)
    if movies_found >= 3 and episodes_found >= 2:
        time.sleep(3)  # let the scan finish writing media info
        break
    time.sleep(2)

print("link")
link = request("/ponte/me", token=token)
manifest_url = link.get("ManifestUrl") or link.get("manifestUrl")
check("GET /ponte/me returns a manifest URL", bool(manifest_url) and manifest_url.endswith("/manifest.json"), manifest_url)
addon = manifest_url.rsplit("/manifest.json", 1)[0]
check("same link on the second call", request("/ponte/me", token=token).get("ManifestUrl") == manifest_url)

try:
    request(addon.replace(addon.rsplit("/", 1)[1], "0" * 64) + "/manifest.json")
    check("unknown token is rejected", False)
except urllib.error.HTTPError as e:
    check("unknown token is rejected", e.code == 404, str(e.code))

print("manifest")
m = request(addon + "/manifest.json")
catalogs = {(c["type"], c["id"]): c["name"] for c in m["catalogs"]}
check("manifest has id, name and resources", m["id"].startswith("cc.ponte.") and len(m["resources"]) >= 3, m["name"])
check("a catalog per library", any(i.startswith("ponte.lib.") for _, i in catalogs), ", ".join(catalogs.values()))
_, _, raw = request(addon + "/manifest.json", raw=True)
check("camelCase JSON", b'"idPrefixes"' in raw)

print("catalogs")
movie_lib = next(i for t, i in catalogs if t == "movie" and i.startswith("ponte.lib."))
show_lib = next(i for t, i in catalogs if t == "series" and i.startswith("ponte.lib."))
movies = request(f"{addon}/catalog/movie/{movie_lib}.json")["metas"]
names = {x["name"]: x for x in movies}
check("movie library lists both movies", len(movies) == 2, ", ".join(names))
bbb = next((x for x in movies if x["id"] == "tt1254207"), None)
check("IMDb id from the folder name", bbb is not None)
home = next((x for x in movies if x["id"].startswith("ponte:")), None)
check("movie without IMDb gets a ponte: id", home is not None, home and home["id"])
recent = request(f"{addon}/catalog/movie/ponte.recent.json")["metas"]
check("recently added lists movies", len(recent) == 2)
search = request(f"{addon}/catalog/movie/{movie_lib}/search=bunny.json")["metas"]
check("search finds Big Buck Bunny", [x["id"] for x in search] == ["tt1254207"], str([x["name"] for x in search]))
page2 = request(f"{addon}/catalog/movie/{movie_lib}/skip=100.json")["metas"]
check("skip past the end is empty", page2 == [])
shows = request(f"{addon}/catalog/series/{show_lib}.json")["metas"]
check("show library lists the series", [x["id"] for x in shows] == ["tt0944947"], str([x["name"] for x in shows]))

print("meta")
meta = request(f"{addon}/meta/series/tt0944947.json")["meta"]
episode_ids = [v["id"] for v in meta.get("videos", [])]
check("series meta has episodes", episode_ids == ["tt0944947:1:1", "tt0944947:1:2"], str(episode_ids))
movie_meta = request(f"{addon}/meta/movie/{home['id']}.json")["meta"]
check("meta by ponte: id", movie_meta is not None and movie_meta["id"] == home["id"], movie_meta and movie_meta["name"])

print("streams")
streams = request(f"{addon}/stream/movie/tt1254207.json")["streams"]
check("both versions of the movie", len(streams) == 2, " / ".join(s["name"].replace("\n", " ") for s in streams))
check("highest resolution first", "2160p" in streams[0]["name"] and "1080p" in streams[1]["name"])
check("HEVC and 10bit described", "HEVC" in streams[0]["description"] and "10bit" in streams[0]["description"], streams[0]["description"].replace("\n", " | "))
check("audio language described", "Portuguese" in streams[1]["description"], streams[1]["description"].replace("\n", " | "))
check("filename hint", streams[1]["behaviorHints"]["filename"].endswith(".mkv"))
check("subtitle variants described", "Portuguese (Brazil) Forced" in streams[1]["description"], streams[1]["description"].replace("\n", " | "))

print("aiostreams data")
data = streams[1]["streamData"]
parsed = data["parsedFile"]
check("marked as a library http stream", data["library"] is True and data["type"] == "http")
check("title and year", parsed["title"] == "Big Buck Bunny" and parsed["year"] == "2008", f'{parsed["title"]} {parsed.get("year")}')
check("resolution and encode", parsed["resolution"] == "1080p" and parsed["encode"] == "AVC")
check("audio tags and language", parsed["audioTags"] == ["AAC"] and parsed["languages"] == ["Portuguese"], str(parsed["languages"]))
check("subtitle languages", parsed["subtitles"] == ["Portuguese (Brazil)"], str(parsed["subtitles"]))
tracks = parsed["subtitleTracks"]
check("only embedded tracks, numbered as in the file", [t.get("index") for t in tracks] == [2, 3], str(tracks))
check("forced track flagged", tracks[1].get("forced") is True, str(tracks[1]))
episode_data = request(f"{addon}/stream/series/tt0944947:1:2.json")["streams"][0]["streamData"]["parsedFile"]
check("episode numbers", episode_data["seasons"] == [1] and episode_data["episodes"] == [2], str(episode_data.get("episodes")))
ep = request(f"{addon}/stream/series/tt0944947:1:2.json")["streams"]
check("episode stream", len(ep) == 1 and "S01E02" in ep[0]["description"])
none = request(f"{addon}/stream/series/tt0944947:9:9.json")["streams"]
check("missing episode has no streams", none == [])
other = request(f"{addon}/stream/movie/tt0000001.json")["streams"]
check("title not in the library has no streams", other == [])
kitsu = request(f"{addon}/stream/series/kitsu:1:1.json")["streams"]
check("unsupported id prefix is ignored", kitsu == [])

print("playback")
status, headers, body = request(streams[1]["url"], headers={"Range": "bytes=0-1023"}, raw=True)
check("range request returns 206", status == 206, headers.get("Content-Range"))
check("returns the requested bytes", len(body) == 1024)
check("video content type", headers.get("Content-Type", "").startswith("video/"), headers.get("Content-Type"))

print("subtitles")
subs = request(f"{addon}/subtitles/movie/tt1254207.json")["subtitles"]
check("external subtitle offered", len(subs) == 1 and subs[0]["lang"] == "pob", str(subs))
def subtitles_for(extra):
    return request(f"{addon}/subtitles/movie/tt1254207/{extra}.json")["subtitles"]
check("the file with the subtitle gets it", len(subtitles_for("filename=Big%20Buck%20Bunny%20%282008%29%20-%201080p.mkv")) == 1)
check("another copy does not", subtitles_for("filename=Big%20Buck%20Bunny%20%282008%29%20-%202160p.mkv") == [])
check("an unknown file gets every copy's", len(subtitles_for("filename=renamed.mkv")) == 1)
check("external subtitle labelled", subs[0].get("label") == "External · Ponte", str(subs[0].get("label")))
if subs:
    _, _, sub = request(subs[0]["url"], raw=True)
    check("subtitle file served", "Ponte" in sub.decode("utf-8", "replace"))

print("reset")
new = request("/ponte/me/reset", "POST", token=token)
new_url = new.get("ManifestUrl") or new.get("manifestUrl")
check("reset gives a new link", new_url != manifest_url)
try:
    request(manifest_url)
    check("old link stops working", False)
except urllib.error.HTTPError as e:
    check("old link stops working", e.code == 404)

print()
print("all checks passed" if failures == 0 else f"{failures} check(s) failed")
sys.exit(1 if failures else 0)
