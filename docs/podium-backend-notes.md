# Podium Browser backend — verified notes

Reference notes for the Rhino plug-in, kept because the plug-in talks to this
backend directly. Everything here was verified against the live site rather than
read from documentation; dates are given where it matters.

Extracted from the (now removed) redesign spec so the findings survive
independently of whether that rewrite is ever picked up again.

---

## API contract (verified 2026-07-03)

All endpoints are same-origin GET and return JSON — but are served as
`text/html`, so parse defensively rather than trusting the content type.

| Endpoint | Shape | Notes |
|---|---|---|
| `categories.php` | `{ parentId, primaryIndex, id, show:{skp,3dm}, title }[]` | 321 rows; tree via `parentId`; filter to `show[format] === true` |
| `items.php` | `{ id, hash, title, tags[], fileExt, fileSize, thumbnailExt, isFree, uploadDate }[]` | **The entire catalog in one payload.** Paging params are ignored |
| `relationships.php` | `{ id, itemId, categoryId }[]` | ~150k rows; item↔category is **many-to-many** |
| `validate.php`, `activate.php`, `deactivate.php` | license | `activate` → `{ statusCode:201, key, fingerprint, id, expiry:{y,m,d} }` |

**Asset URLs**

- Thumbnail: `/images/files/{hash[:2]}/{hash}.{thumbnailExt}`
- Download: `/.secret/files/{hash}.{fileExt}`

**Backends**

- `rhino3d.pdm-plants-textures.com` — 20,365 items, all `.3dm`. This is what the
  Rhino plug-in points at by default.
- `v4.pdm-plants-textures.com` — 37,875 items, mostly `.skp` (plus 43 `.hdr`).
  Rhino 8 imports `.skp` natively, which is why the panel can switch to it.

**Data model.** DynamoDB tables `Categories`, `Items`, `Relationships` hold
**metadata only**; the binaries live separately and are joined by `hash`.

**One item is always one file** (Rhino catalog, checked 2026-09-20). All 20,365
items carry exactly nine fields, every `hash` is unique to a single item, and
every `fileExt` is `3dm`. There is no field that could express a multi-part item.
Re-check before assuming the same of the `.skp` catalog.

---

## Finding: paid assets are unprotected (2026-09-14)

Found while verifying that download URLs built by the plug-in actually resolve.
A paid item was checked as a control, expecting it to be refused.

**What was observed.** `GET https://rhino3d.pdm-plants-textures.com/.secret/files/{hash}.3dm`
returns the full model for paid items — anonymous request, no cookie, no licence
key, generic user agent. Three paid items were checked. All returned `200` with
the complete `Content-Length`, and range requests returned real Rhino geometry
(the files begin `3D Geometry File Format`). One was 15.1 MB.

**Why it matters.** The hashes are not secret either. `items.php` is a public
endpoint returning the whole catalog, hashes included — a plain script pulled all
20,365 records with nothing but a `User-Agent` header. A public hash list plus an
unauthenticated asset server means the entire paid Rhino library is downloadable
by anyone who reads the catalog JSON. The `.secret/` path segment is the only
thing in the way, and an unguessable path protects nothing once the paths are
published.

**Consequence for any licence work.** The licence check in the existing
front-ends decides which buttons to show; the server hands the files to anyone
who asks. Any licence gate built into a client reproduces that — it looks like
protection without being protection.

**Options, roughly by effort.** This is a backend change, so it is Cadalog's
call:

1. **Signed, expiring URLs.** `validate.php` issues a short-lived signed URL per
   asset; the asset origin rejects unsigned requests. Standard for CloudFront/S3
   and the smallest change that actually works. Clients ask for a URL instead of
   constructing one.
2. **Authenticated download endpoint.** Route downloads through PHP that checks
   the key and streams the file. Simpler to reason about, but puts asset traffic
   through the app server.
3. **Split free and paid origins.** Keep free assets public; move the paid
   library behind either of the above. Smallest blast radius for existing
   clients.

Doing nothing is a defensible business decision — but it should be a decision,
not an accident of the URL scheme.

**Not checked:** whether the `v4` (SketchUp) catalog behaves the same way.
Likely, since it is the same codebase and URL scheme, but it was not tested.
