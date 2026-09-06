# StaticS3
Serve static files from an S3 bucket

## Secure file transfer

**Threat model:** the server only ever sees ciphertext. The browser seals each file with a key derived from ECDH P-256 against the recipient's public key (HKDF-SHA256, then AES-256-GCM in 4 MiB frames), so the service stores an opaque blob and never sees the plaintext, the original filename, or a key that could open it. What the server does see is the ciphertext's size and when it arrived.

The signed upload token is a bearer capability: it proves only that its holder may upload up to a byte limit before an expiry. It is **not single use** — anyone holding the link can upload repeatedly until it expires, and there is no revocation short of rotating `Transfer:SigningKey` (which invalidates every outstanding link). Keep TTLs short and treat the link as a secret.

Because the token lives in the URL *fragment* and is sent in an `X-Upload-Token` header, it is not written to server access logs or leaked through `Referer`.

### Configuration

| Setting | Env var | Purpose |
|---|---|---|
| `Transfer:SigningKey` | `TRANSFER_SIGNING_KEY` | HMAC key (>= 32 chars) used to sign upload tokens. |
| `Transfer:AdminToken` | `TRANSFER_ADMIN_TOKEN` | Bearer token (>= 16 chars) for `X-Admin-Token` on admin endpoints. |
| `Transfer:BucketName` | `TRANSFER_BUCKET_NAME` | A **private** R2/S3 bucket for uploads. Must differ from `R2:BucketName` — the static bucket is publicly readable and must never receive customer uploads. |
| `Transfer:KeyPrefix` | `TRANSFER_KEY_PREFIX` | Object key prefix, default `transfers`. |
| `Transfer:MaxBytes` | `TRANSFER_MAX_BYTES` | Upper bound for any ticket's upload size, default 256 MiB. |
| `Transfer:DefaultTtlSeconds` / `Transfer:MaxTtlSeconds` | `TRANSFER_DEFAULT_TTL_SECONDS` / `TRANSFER_MAX_TTL_SECONDS` | Link lifetime, default 48h / max 14d. |
| `Transfer:PublicBaseUrl` | `TRANSFER_PUBLIC_BASE_URL` | Base URL used to build the returned `url`; falls back to the incoming request's scheme/host. |

The feature is disabled (all `/transfer/*` and `/transfer-admin/*` routes return 404) until `SigningKey`, `AdminToken` and `BucketName` are all set. A warning is logged at startup naming whichever of the three is missing.

A value set in the `Transfer` section of `appsettings.json` takes precedence over the matching `TRANSFER_*` environment variable, so that section deliberately ships only the three (blank) secrets — a non-blank default there would shadow the environment variable and silently ignore it.

### Route split: public vs. admin

`/transfer/` is the only prefix the public Ingress (`transfer.coflnet.com`) routes to this pod — it carries just `GetLink` and `Upload`, the two endpoints a customer's browser calls. The admin API (`CreateLink`, `ListUploads`, `DownloadUpload`) lives under a separate `/transfer-admin/` prefix on `TransferAdminController`, which the Ingress does not route to, so it stays cluster-internal even though both controllers run in the same pod. `/objects/` (the public static-object API) is likewise never exposed by that Ingress rule.

Reach the admin API by port-forwarding into the cluster instead:

```
kubectl port-forward svc/static-s3 8000:8000 -n s3
```

then target `http://localhost:8000/transfer-admin/...` as shown below.

### 0. Generating the key pair

Two options, strongest first:

- **In the browser** (recommended): open `/transfer-admin/keys.html` (or use the admin console below), generate a pair, save the private key, and pass the public key as `publicKey` when creating the link. The private key never touches the server.
- **Server-generated**: omit `publicKey` and the server generates a pair, returns the private key **once** in the create response, and keeps no copy. Convenient for scripting, but the private key travels over the wire to you.

### 1. Admin creates an upload link

```
curl -X POST http://localhost:8000/transfer-admin/links \
  -H "X-Admin-Token: $TRANSFER_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"ttlSeconds": 86400, "maxBytes": 104857600, "label": "contract for Acme"}'
```

Response (`201`):

```json
{
  "id": "AbCdEfGh12Q",
  "token": "v1.eyJ...b64url...",
  "url": "https://static.example.com/transfer/#v1.eyJ...b64url...",
  "decryptPath": "/transfer-admin/decrypt.html",
  "expiresAt": "2026-09-07T12:00:00+00:00",
  "maxBytes": 104857600,
  "label": "contract for Acme",
  "publicKey": "BJ3f...b64url...",
  "privateKeyJwk": "{\"kty\":\"EC\",\"crv\":\"P-256\",\"d\":\"...\",\"x\":\"...\",\"y\":\"...\",\"ext\":true,\"key_ops\":[\"deriveBits\",\"deriveKey\"]}",
  "note": "The private key is shown only once here and is never logged or stored by the server - save it now."
}
```

`privateKeyJwk` is only returned when the server generated the key pair (i.e. no `publicKey` was supplied in the request) and is never logged or persisted. Save it immediately — it is required to decrypt the upload later.

### 2. Customer uploads

Send the customer the `url`. Opening it loads the transfer web UI, which reads the token from the URL fragment, encrypts the selected file in the browser using the ticket's `publicKey`, and `POST`s the ciphertext to `/transfer/upload` with header `X-Upload-Token: <token>`. The server stores the ciphertext as an opaque blob — it never sees plaintext, a filename, or the label.

### 3. Admin lists/downloads uploads

```
curl http://localhost:8000/transfer-admin/uploads?id=AbCdEfGh12Q \
  -H "X-Admin-Token: $TRANSFER_ADMIN_TOKEN"

curl http://localhost:8000/transfer-admin/uploads/transfers/AbCdEfGh12Q/20260906T120000Z-Ab12Cd.bin \
  -H "X-Admin-Token: $TRANSFER_ADMIN_TOKEN" -o ciphertext.bin
```

### 4. Decrypting

Open `decryptPath` (`/transfer-admin/decrypt.html`, reachable only through the port-forward above), paste in the private key JWK, and load the downloaded ciphertext file — it is decrypted entirely in the browser and the original filename is restored. Nothing is uploaded and the key is not persisted in browser storage.

### Admin console

`http://localhost:8000/transfer-admin/` (after the port-forward above) is a single-page console that wraps steps 0-3 in a form instead of curl:

- **Admin token** — enter the `X-Admin-Token` once; it is kept in `sessionStorage` for the tab (never `localStorage`) and sent on every admin request, with a "Forget token" button to drop it again.
- **Create an upload link** — a form for `label`, TTL, and max size, plus a choice between generating a fresh key pair in the browser (default; only the public key is ever posted) or reusing a previously saved one. On success it shows the `url` with a copy button and stores the new key pair locally, tagged with the returned ticket `id`.
- **Saved keys** — the key pairs generated by this browser, kept in `localStorage`. Only this storage and a downloaded `.jwk.json` backup hold the private key; the server never has a copy, and clearing site data or forgetting a saved key without a backup makes its uploads permanently unrecoverable.
- **Received uploads** — lists objects via `GET /transfer-admin/uploads` (optionally filtered by ticket id), and for each one offers **Decrypt & save** when the matching key is saved locally (fetches the ciphertext, decrypts it in the browser with a progress bar, and hands back the original file), or **Download ciphertext** with a pointer to `decrypt.html` when it is not.

`keys.html` and `decrypt.html` are linked from the console for standalone use, and link back to it in turn.

### Envelope format

`wwwroot/transfer/crypto.js` documents the on-disk layout. Each frame's AES-GCM IV and additional data bind it to its frame index and to the header, and the encrypted metadata frame declares how many frames follow, so reordering, splicing, truncation, and cross-ticket substitution all fail to decrypt rather than silently returning partial data.

The browser-side crypto has its own test suite:

```
node --test 'tests/*.test.mjs'
```

It covers round-trips (single chunk, many chunks, empty), wrong key, tampered header, tampered ciphertext, truncation, trailing data, and frame reordering. It is not run by the container CI, which builds and runs the .NET tests only.

