import test from 'node:test';
import assert from 'node:assert/strict';

// Separate module instances exercise native and browser initialization independently.
test('native storage awaits durable vault writes and never falls back on failure', async () => {
    let finish;
    globalThis.coflnetTransfer = {
        readKeys: async () => [{ id: 'retained' }],
        saveKeys: () => new Promise(resolve => { finish = resolve; }),
        download: async (blob, name) => [await blob.text(), name],
        copy: async text => text,
    };
    globalThis.localStorage = {
        getItem() { throw new Error('native must not read browser storage'); },
        setItem() { throw new Error('native must not write browser storage'); },
    };
    const native = await import('../wwwroot/transfer-admin/mobile.js?native');
    assert.deepEqual(await native.readKeys(), [{ id: 'retained' }]);
    let saved = false;
    const pending = native.saveKeys([{ id: 'new' }]).then(() => { saved = true; });
    await Promise.resolve();
    assert.equal(saved, false);
    finish();
    await pending;
    assert.equal(saved, true);
    globalThis.coflnetTransfer.saveKeys = async () => { throw new Error('locked'); };
    await assert.rejects(native.saveKeys([]), /locked/);
    assert.deepEqual(await native.download(new Blob(['file']), 'report.txt'), ['file', 'report.txt']);
    assert.equal(await native.copy('upload-link'), 'upload-link');
    delete globalThis.coflnetTransfer;
});

test('ordinary browser keeps its existing persisted key format', async () => {
    let stored = '[{"id":"existing"}]';
    globalThis.localStorage = {
        getItem(key) { assert.equal(key, 'statics3.transfer.keys'); return stored; },
        setItem(key, value) { assert.equal(key, 'statics3.transfer.keys'); stored = value; },
    };
    const browser = await import('../wwwroot/transfer-admin/mobile.js?browser');
    assert.deepEqual(await browser.readKeys(), [{ id: 'existing' }]);
    await browser.saveKeys([{ id: 'new' }]);
    assert.deepEqual(JSON.parse(stored), [{ id: 'new' }]);
    delete globalThis.localStorage;
});
