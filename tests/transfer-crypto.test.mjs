// Round-trip and tamper tests for the browser transfer envelope.
// Run with: node --test tests/
import test from 'node:test';
import assert from 'node:assert/strict';

import {
    decryptFile,
    encryptFile,
    fromBase64Url,
    generateKeyPair,
    sanitizeName,
    toBase64Url
} from '../wwwroot/transfer/crypto.js';

const bytes = (length, seed = 7) => {
    const result = new Uint8Array(length);
    for (let index = 0; index < length; index++)
        result[index] = (index * 31 + seed) & 0xff;
    return result;
};

const asFile = (content, name = 'report.pdf', type = 'application/pdf') =>
    new File([content], name, { type });

const corrupt = async (blob, offset) => {
    const raw = new Uint8Array(await blob.arrayBuffer());
    raw[offset] ^= 0xff;
    return new Blob([raw]);
};

test('round-trips a single chunk file', async () => {
    const keys = await generateKeyPair();
    const content = bytes(1024);
    const sealed = await encryptFile(asFile(content), keys.publicKey);
    const opened = await decryptFile(sealed, keys.privateKeyJwk);

    assert.equal(opened.name, 'report.pdf');
    assert.equal(opened.type, 'application/pdf');
    assert.equal(opened.size, content.length);
    assert.deepEqual(new Uint8Array(await opened.blob.arrayBuffer()), content);
});

test('round-trips a file spanning many chunks', async () => {
    const keys = await generateKeyPair();
    const content = bytes(5000, 3);
    const sealed = await encryptFile(asFile(content), keys.publicKey, { chunkSize: 512 });
    const opened = await decryptFile(sealed, keys.privateKeyJwk);

    assert.equal(opened.size, 5000);
    assert.deepEqual(new Uint8Array(await opened.blob.arrayBuffer()), content);
});

test('round-trips an empty file', async () => {
    const keys = await generateKeyPair();
    const sealed = await encryptFile(asFile(new Uint8Array(0), 'empty.txt', 'text/plain'), keys.publicKey);
    const opened = await decryptFile(sealed, keys.privateKeyJwk);

    assert.equal(opened.size, 0);
    assert.equal(opened.name, 'empty.txt');
});

test('reports encryption and decryption progress up to the file size', async () => {
    const keys = await generateKeyPair();
    const content = bytes(2500);
    const encrypted = [];
    const decrypted = [];
    const sealed = await encryptFile(asFile(content), keys.publicKey, {
        chunkSize: 1000,
        onProgress: (done, total) => encrypted.push([done, total])
    });
    await decryptFile(sealed, keys.privateKeyJwk, { onProgress: (done, total) => decrypted.push([done, total]) });

    assert.deepEqual(encrypted.at(-1), [2500, 2500]);
    assert.deepEqual(decrypted.at(-1), [2500, 2500]);
});

test('accepts the private key as a JSON string', async () => {
    const keys = await generateKeyPair();
    const sealed = await encryptFile(asFile(bytes(64)), keys.publicKey);
    const opened = await decryptFile(sealed, JSON.stringify(keys.privateKeyJwk));

    assert.equal(opened.size, 64);
});

test('a different private key cannot decrypt', async () => {
    const keys = await generateKeyPair();
    const other = await generateKeyPair();
    const sealed = await encryptFile(asFile(bytes(64)), keys.publicKey);

    await assert.rejects(() => decryptFile(sealed, other.privateKeyJwk));
});

test('a tampered ciphertext byte is rejected', async () => {
    const keys = await generateKeyPair();
    const sealed = await encryptFile(asFile(bytes(256)), keys.publicKey);
    const tampered = await corrupt(sealed, sealed.size - 30);

    await assert.rejects(() => decryptFile(tampered, keys.privateKeyJwk));
});

test('a tampered header byte is rejected', async () => {
    const keys = await generateKeyPair();
    const sealed = await encryptFile(asFile(bytes(256)), keys.publicKey);

    // Flip a salt byte: the header is bound into every frame as additional data.
    const tampered = await corrupt(sealed, 80);

    await assert.rejects(() => decryptFile(tampered, keys.privateKeyJwk));
});

test('a truncated file is rejected', async () => {
    const keys = await generateKeyPair();
    const content = bytes(3000);
    const sealed = await encryptFile(asFile(content), keys.publicKey, { chunkSize: 512 });

    await assert.rejects(
        () => decryptFile(sealed.slice(0, sealed.size - 600), keys.privateKeyJwk),
        /truncated|corrupted/i);
});

test('trailing data is rejected', async () => {
    const keys = await generateKeyPair();
    const sealed = await encryptFile(asFile(bytes(128)), keys.publicKey);

    await assert.rejects(
        () => decryptFile(new Blob([sealed, new Uint8Array(16)]), keys.privateKeyJwk),
        /trailing|corrupted/i);
});

test('swapping two chunks is rejected', async () => {
    const keys = await generateKeyPair();
    const sealed = await encryptFile(asFile(bytes(1024)), keys.publicKey, { chunkSize: 512 });
    const raw = new Uint8Array(await sealed.arrayBuffer());
    // Frame 0 is the metadata; locate the two equally sized data frames after it.
    const metadataLength = new DataView(raw.buffer).getUint32(111);
    const first = 111 + 4 + metadataLength;
    const frameLength = new DataView(raw.buffer).getUint32(first);
    const second = first + 4 + frameLength;
    const swapped = new Uint8Array(raw);
    swapped.set(raw.subarray(second, second + 4 + frameLength), first);
    swapped.set(raw.subarray(first, first + 4 + frameLength), second);

    await assert.rejects(() => decryptFile(new Blob([swapped]), keys.privateKeyJwk));
});

test('a non-envelope file is rejected before any key work', async () => {
    const keys = await generateKeyPair();

    await assert.rejects(
        () => decryptFile(new Blob([bytes(400)]), keys.privateKeyJwk),
        /not a StaticS3 transfer file/);
});

test('rejects a public key that is not a raw P-256 point', async () => {
    await assert.rejects(
        () => encryptFile(asFile(bytes(8)), toBase64Url(bytes(65))),
        /raw P-256 point/);
    await assert.rejects(
        () => encryptFile(asFile(bytes(8)), toBase64Url(bytes(32))),
        /raw P-256 point/);
});

test('base64url survives a round trip without padding', () => {
    const raw = bytes(65);
    const encoded = toBase64Url(raw);

    assert.ok(!encoded.includes('=') && !encoded.includes('+') && !encoded.includes('/'));
    assert.deepEqual(fromBase64Url(encoded), raw);
});

test('sanitizes hostile file names', () => {
    // Separators become underscores first, then any leading dots are dropped.
    assert.equal(sanitizeName('../../etc/passwd'), '_.._etc_passwd');
    assert.equal(sanitizeName('C:\\windows\\system32'), 'C:_windows_system32');
    assert.equal(sanitizeName(''), 'decrypted.bin');
    assert.equal(sanitizeName('a'.repeat(400)).length, 120);
});
