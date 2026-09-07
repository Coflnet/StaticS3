// The Android console injects this bridge before page scripts run. Ordinary
// browsers retain their existing storage/download behavior.
export const mobile = globalThis.coflnetTransfer;
export const keyStorageName = 'statics3.transfer.keys';
globalThis.coflnetTransferReady = true;

export async function readKeys() {
    if (mobile) return await mobile.readKeys();
    const raw = localStorage.getItem(keyStorageName);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
}

export async function saveKeys(keys) {
    if (mobile) await mobile.saveKeys(keys);
    else localStorage.setItem(keyStorageName, JSON.stringify(keys));
}

export async function download(blob, name) {
    if (mobile) return await mobile.download(blob, name);
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 5000);
}

export async function copy(text) {
    if (mobile) return await mobile.copy(text);
    return await navigator.clipboard.writeText(text);
}
