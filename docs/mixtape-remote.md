# Mixtape Remote API v1 (OrbitOverlay ⇄ Mixtape)

Erik 10-19: "lehetne egy fullos mixtape overlay mint kinyíló widget - a dallistám, lyrics, meg minden főbb funkció". OrbitOverlay's watch
gets a Mixtape widget that opens into a full player: now playing, the Up Next queue, the song list / albums / artists / playlists,
synced lyrics. Mixtape serves this local API; OrbitOverlay is the client.

## Transport

- Named pipe `\\.\pipe\Mixtape.Remote.v1`, duplex, message = one UTF-8 JSON object per line (`\n`), no BOM.
- Access: the current user only (pipe ACL = the owner). OrbitOverlay runs elevated (admin) - the ACL must let the same user's elevated
  process connect (grant the user's SID, not "the same integrity level").
- One client at a time is enough; when it disconnects, the server waits for the next one. Requests on one connection are answered in
  order. The server never blocks Mixtape's UI thread for long: it reads on a background thread and marshals to the UI thread only to read
  state / run a command (`BeginInvoke`/`Invoke` with a short timeout); big payloads (library, covers) are built from data already loaded.
- The pipe exists only while Mixtape runs. The client polls / reconnects every 2 s when it is not there.

## Messages

Request: `{"id": 7, "cmd": "state"}` (+ arguments). Response: `{"id": 7, "ok": true, ...}` or `{"id": 7, "ok": false, "error": "..."}`.
Unknown `cmd` → `ok: false`. Ids are the client's (echoed back).

### hello
→ `{"app": "Mixtape", "version": "0.23.0", "api": 1, "library": "iPod name" | "Local Music" | null}`

### state  (the client polls it ~4 times a second while the widget shows)
```
{
  "v": 123,                      // moves on any change below except positionMs
  "playing": true,
  "track": { "id": "18446744073709551615", "title": "...", "artist": "...", "album": "...", "durationMs": 241000,
             "rating": 4, "cover": "<coverKey>" } | null,
  "positionMs": 61234,
  "volume": 0.8,                 // 0..1
  "shuffle": false,
  "repeat": "off" | "all" | "one",
  "queue": [ { "id": "...", "title": "...", "artist": "...", "durationMs": 200000, "cover": "<coverKey>" } ],   // Up Next, max 100
  "queueV": 12,                  // moves when the queue changes
  "libraryV": 3,                 // moves when the song list / playlists change (the client then asks "library" again)
  "lyricsV": 5                   // moves when the playing track's lyrics change (new track, lyrics found)
}
```
- `id` = the track's persistent identity (`Track.Dbid` as a decimal string; for a Local Music file: a stable string of its path).
- `cover` = an opaque key for the album art (same key for the tracks of one album); null when the track has none.

### library
```
{
  "v": 3,
  "songs": [ { "id": "...", "title": "...", "artist": "...", "album": "...", "albumArtist": "...", "durationMs": 1,
               "track": 1, "disc": 1, "year": 2001, "cover": "<coverKey>" } ],
  "playlists": [ { "key": "...", "name": "...", "songs": ["<id>", ...] } ]      // the library's own playlists (not the master)
}
```
The client groups albums / artists itself. Songs in the library's order (as Mixtape lists them).

### cover  `{"cmd": "cover", "key": "<coverKey>", "size": 256}`
→ `{"w": 256, "h": 256, "bgra": "<base64>"}` - the art scaled to fit a size × size square (aspect kept, centred, transparent around),
32-bit BGRA, premultiplied alpha, rows top to bottom. `size` ∈ {64, 128, 256}. Unknown key → `ok: false`.

### lyrics
→ `{"id": "<track id>", "synced": true, "lines": [ { "ms": 12340, "text": "..." } ]}` for the playing track (`synced: false` → `ms` 0 and
the plain text split into lines); no lyrics → `"lines": []`. Whatever Mixtape's own lyrics panel would show for it (the file's tags, a
sidecar .lrc, a cached online result) - the API never starts an online lookup by itself.

### Commands (→ `{"ok": true}`; the next `state` shows the result)
| cmd | arguments | does |
|---|---|---|
| `play` / `pause` / `toggle` | | as the play button |
| `next` / `prev` | | as the buttons (prev: restart if > 3 s in, like Mixtape does) |
| `seek` | `ms` | position in the playing track |
| `volume` | `v` (0..1) | Mixtape's volume |
| `shuffle` | `on` (bool) | |
| `repeat` | `mode` ("off"/"all"/"one") | |
| `playSongs` | `ids` [..], `start` (index) | plays `ids[start]` now and puts the rest of the list (in order, the ones after `start`) as Up Next, replacing it - an album, a playlist, the song list from VR |
| `queueAdd` | `ids` | to the end of Up Next |
| `queueNext` | `ids` | to the front of Up Next (play next) |
| `queueRemove` | `index` | |
| `queueMove` | `from`, `to` | |
| `queueClear` | | |
| `queueJump` | `index` | plays that queue entry now (the entries before it are dropped) |
| `rate` | `stars` (0..5) | the playing song's rating |

All commands act exactly as the matching button / menu in Mixtape would (play counts, scrobble-like "Listening" stats, SMTC, Discord
presence all as usual).
