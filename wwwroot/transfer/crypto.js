// End-to-end encryption for StaticS3 secure transfers.
//
// Envelope layout (all integers big-endian):
//
//   header (111 bytes)
//     0..3     magic "S3TX"
//     4        version (1)
//     5        ephemeral public key length (65)
//     6..70    ephemeral P-256 public key, raw uncompressed point
//     71..102  HKDF salt (32 bytes)
//     103..110 nonce prefix (8 bytes)
//   frames, repeated until the end of the file
//     4 bytes  ciphertext length
//     n bytes  AES-256-GCM ciphertext including the 16 byte tag
//
// Frame 0 carries the metadata JSON, frames 1..chunks carry the file itself.
// The content key is HKDF-SHA256(ECDH(ephemeral, recipient), salt, "StaticS3-transfer-v1" || epk).
// Frame i uses IV = noncePrefix || uint32(i) and AAD = header || uint32(i), which binds every
// frame to its position and to the header, so reordering or splicing frames fails to decrypt.
// The metadata frame states how many data frames follow, so truncation is detected too.

const MAGIC = [0x53, 0x33, 0x54, 0x58]; // "S3TX"
const VERSION = 1;
const PUBLIC_KEY_LENGTH = 65;
const SALT_LENGTH = 32;
const NONCE_LENGTH = 8;
const HEADER_LENGTH = 6 + PUBLIC_KEY_LENGTH + SALT_LENGTH + NONCE_LENGTH; // 111
const SALT_OFFSET = 6 + PUBLIC_KEY_LENGTH;
const NONCE_OFFSET = SALT_OFFSET + SALT_LENGTH;
const INFO_PREFIX = new TextEncoder().encode('StaticS3-transfer-v1');
const MAX_FRAME_LENGTH = 64 * 1024 * 1024;
const MAX_CHUNKS = 1000000;

export const DEFAULT_CHUNK_SIZE = 4 * 1024 * 1024;
export const CURVE = { name: 'ECDH', namedCurve: 'P-256' };

export function toBase64Url(bytes) {
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += 0x8000)
        binary += String.fromCharCode.apply(null, bytes.subarray(offset, offset + 0x8000));
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function fromBase64Url(value) {
    const normalized = String(value).trim().replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized + '='.repeat((4 - (normalized.length % 4)) % 4);
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++)
        bytes[index] = binary.charCodeAt(index);
    return bytes;
}

function concat(...parts) {
    const total = parts.reduce((sum, part) => sum + part.length, 0);
    const result = new Uint8Array(total);
    let offset = 0;
    for (const part of parts) {
        result.set(part, offset);
        offset += part.length;
    }
    return result;
}

function uint32(value) {
    return new Uint8Array([(value >>> 24) & 0xff, (value >>> 16) & 0xff, (value >>> 8) & 0xff, value & 0xff]);
}

function readUint32(bytes, offset) {
    return ((bytes[offset] << 24) >>> 0) + (bytes[offset + 1] << 16) + (bytes[offset + 2] << 8) + bytes[offset + 3];
}

// Generates a recipient key pair. The private key stays with whoever asks for the files.
export async function generateKeyPair() {
    const pair = await crypto.subtle.generateKey(CURVE, true, ['deriveBits', 'deriveKey']);
    const raw = new Uint8Array(await crypto.subtle.exportKey('raw', pair.publicKey));
    const jwk = await crypto.subtle.exportKey('jwk', pair.privateKey);
    return { publicKey: toBase64Url(raw), privateKeyJwk: jwk };
}

function fail(code, message) {
    const error = new Error(message);
    error.code = code;
    return error;
}

export function importRecipientPublicKey(value) {
    const raw = fromBase64Url(value);
    if (raw.length !== PUBLIC_KEY_LENGTH || raw[0] !== 0x04)
        throw fail('crypto.badPublicKey', 'The recipient public key is not a raw P-256 point.');
    return crypto.subtle.importKey('raw', raw, CURVE, false, []);
}

export function importRecipientPrivateKey(jwk) {
    const parsed = typeof jwk === 'string' ? JSON.parse(jwk) : jwk;
    if (!parsed || parsed.kty !== 'EC' || parsed.crv !== 'P-256' || !parsed.d)
        throw fail('crypto.badPrivateKey', 'That is not a P-256 private key in JWK format.');
    // key_ops from another producer may not list what we need; we only ever derive with it.
    const { key_ops, ext, use, alg, ...rest } = parsed;
    return crypto.subtle.importKey('jwk', { ...rest, ext: true }, CURVE, false, ['deriveBits']);
}

async function deriveContentKey(privateKey, publicKey, salt, ephemeralPublic) {
    const shared = await crypto.subtle.deriveBits({ name: 'ECDH', public: publicKey }, privateKey, 256);
    const material = await crypto.subtle.importKey('raw', shared, 'HKDF', false, ['deriveKey']);
    return crypto.subtle.deriveKey(
        { name: 'HKDF', hash: 'SHA-256', salt, info: concat(INFO_PREFIX, ephemeralPublic) },
        material,
        { name: 'AES-GCM', length: 256 },
        false,
        ['encrypt', 'decrypt']);
}

function buildHeader(ephemeralPublic, salt, nonce) {
    return concat(new Uint8Array(MAGIC), new Uint8Array([VERSION, PUBLIC_KEY_LENGTH]), ephemeralPublic, salt, nonce);
}

async function sealFrame(key, header, index, plaintext) {
    const iv = concat(header.subarray(NONCE_OFFSET, NONCE_OFFSET + NONCE_LENGTH), uint32(index));
    const sealed = new Uint8Array(await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv, additionalData: concat(header, uint32(index)) }, key, plaintext));
    return concat(uint32(sealed.length), sealed);
}

async function openFrame(key, header, index, ciphertext) {
    const iv = concat(header.subarray(NONCE_OFFSET, NONCE_OFFSET + NONCE_LENGTH), uint32(index));
    return new Uint8Array(await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv, additionalData: concat(header, uint32(index)) }, key, ciphertext));
}

// Encrypts a File/Blob for the holder of the matching private key. Returns a Blob, which the
// browser keeps on disk, so files much larger than the tab's memory budget still work.
export async function encryptFile(file, recipientPublicKey, options = {}) {
    const chunkSize = options.chunkSize || DEFAULT_CHUNK_SIZE;
    const onProgress = options.onProgress || (() => { });
    const recipient = await importRecipientPublicKey(recipientPublicKey);
    const ephemeral = await crypto.subtle.generateKey(CURVE, true, ['deriveBits']);
    const ephemeralPublic = new Uint8Array(await crypto.subtle.exportKey('raw', ephemeral.publicKey));
    const salt = crypto.getRandomValues(new Uint8Array(SALT_LENGTH));
    const nonce = crypto.getRandomValues(new Uint8Array(NONCE_LENGTH));
    const header = buildHeader(ephemeralPublic, salt, nonce);
    const key = await deriveContentKey(ephemeral.privateKey, recipient, salt, ephemeralPublic);

    const chunks = Math.ceil(file.size / chunkSize);
    const metadata = {
        name: file.name || 'file',
        type: file.type || 'application/octet-stream',
        size: file.size,
        chunks,
        chunkSize
    };
    const parts = [header, await sealFrame(key, header, 0, new TextEncoder().encode(JSON.stringify(metadata)))];
    for (let index = 0; index < chunks; index++) {
        const slice = file.slice(index * chunkSize, Math.min(file.size, (index + 1) * chunkSize));
        parts.push(await sealFrame(key, header, index + 1, new Uint8Array(await slice.arrayBuffer())));
        onProgress(Math.min(file.size, (index + 1) * chunkSize), file.size);
    }
    onProgress(file.size, file.size);
    return new Blob(parts, { type: 'application/octet-stream' });
}

// Filenames come from whoever uploaded the file, so they are treated as hostile input.
export function sanitizeName(name) {
    const cleaned = String(name || '')
        .replace(/[\u0000-\u001f\u007f]/g, '')
        .replace(/[\\/]/g, '_')
        .replace(/^\.+/, '')
        .trim();
    return cleaned.slice(0, 120) || 'decrypted.bin';
}

export async function decryptFile(blob, privateKeyJwk, options = {}) {
    const onProgress = options.onProgress || (() => { });
    if (blob.size < HEADER_LENGTH)
        throw fail('crypto.tooSmall', 'The file is too small to be an encrypted transfer.');
    const header = new Uint8Array(await blob.slice(0, HEADER_LENGTH).arrayBuffer());
    if (MAGIC.some((value, index) => header[index] !== value))
        throw fail('crypto.notEnvelope', 'This is not a StaticS3 transfer file.');
    if (header[4] !== VERSION)
        throw fail('crypto.badVersion', `Unsupported envelope version ${header[4]}.`);
    if (header[5] !== PUBLIC_KEY_LENGTH)
        throw fail('crypto.badHeader', 'The envelope header is malformed.');

    const privateKey = await importRecipientPrivateKey(privateKeyJwk);
    const ephemeralPublic = header.subarray(6, 6 + PUBLIC_KEY_LENGTH);
    const ephemeral = await crypto.subtle.importKey('raw', ephemeralPublic, CURVE, false, []);
    const key = await deriveContentKey(privateKey, ephemeral, header.subarray(SALT_OFFSET, SALT_OFFSET + SALT_LENGTH), ephemeralPublic);

    let offset = HEADER_LENGTH;
    const next = async (index) => {
        const lengthBytes = new Uint8Array(await blob.slice(offset, offset + 4).arrayBuffer());
        if (lengthBytes.length !== 4)
            throw fail('crypto.truncated', 'The file is truncated.');
        const length = readUint32(lengthBytes, 0);
        if (length < 17 || length > MAX_FRAME_LENGTH)
            throw fail('crypto.corrupted', 'The file is corrupted.');
        offset += 4;
        const ciphertext = await blob.slice(offset, offset + length).arrayBuffer();
        if (ciphertext.byteLength !== length)
            throw fail('crypto.truncated', 'The file is truncated.');
        offset += length;
        return openFrame(key, header, index, ciphertext);
    };

    const metadata = JSON.parse(new TextDecoder().decode(await next(0)));
    const chunks = Number(metadata.chunks);
    const chunkSize = Number(metadata.chunkSize);
    const size = Number(metadata.size);
    if (!Number.isInteger(chunks) || chunks < 0 || chunks > MAX_CHUNKS
        || !Number.isInteger(chunkSize) || chunkSize <= 0
        || !Number.isInteger(size) || size < 0 || size > chunks * chunkSize)
        throw fail('crypto.badMetadata', 'The file metadata is invalid.');

    const parts = [];
    let done = 0;
    for (let index = 1; index <= chunks; index++) {
        const plain = await next(index);
        parts.push(plain);
        done += plain.length;
        onProgress(done, size);
    }
    if (offset !== blob.size)
        throw fail('crypto.trailing', 'The file has unexpected trailing data.');
    if (done !== size)
        throw fail('crypto.sizeMismatch', 'The decrypted size does not match the metadata.');
    return {
        blob: new Blob(parts, { type: metadata.type || 'application/octet-stream' }),
        name: sanitizeName(metadata.name),
        type: metadata.type || 'application/octet-stream',
        size
    };
}
