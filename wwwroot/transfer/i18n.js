// Minimal locale support for the transfer pages.
//
// Customers receive these links by mail and may not speak English, so the UI follows the
// browser's language. German is fully translated; anything else falls back to English.
// Strings are keyed so the crypto module can raise a stable `code` and let the page decide
// the wording, rather than surfacing an untranslated technical message.

export const DICTIONARIES = {
    en: {
        'upload.title': 'Secure file upload',
        'upload.checking': 'Checking your upload link...',
        'upload.ready': 'Your files are encrypted in this browser before they leave your device.',
        'upload.error.noToken': 'This link is missing its upload token. Please use the full link you were sent.',
        'upload.error.unreachable': 'Could not reach the server. Check your connection and try again.',
        'upload.error.expired': 'This upload link has expired. Please ask for a new one.',
        'upload.error.invalid': 'This upload link is not valid. Please ask for a new one.',
        'upload.fact.for': 'For',
        'upload.fact.expires': 'Link expires',
        'upload.fact.max': 'Max file size',
        'upload.defaultLabel': 'File transfer',
        'upload.drop.title': 'Choose files or drop them here',
        'upload.drop.hint': 'Every file is encrypted on this device before it is sent.',
        'upload.footer': "Files are sealed with the recipient's public key using ECDH P-256 and AES-256-GCM. The server stores only ciphertext and never receives a key that can open it.",
        'upload.status.encrypting': 'Encrypting on this device...',
        'upload.status.uploading': 'Uploading...',
        'upload.status.done': 'Encrypted and delivered.',
        'upload.status.receipt': 'receipt {hash} - {size} stored',
        'upload.error.tooLarge': 'Too large. This link accepts up to {size}.',
        'upload.error.tooLargeSealed': 'Too large once encrypted. This link accepts up to {size}.',
        'upload.error.rejectedSize': 'The file is larger than this link allows.',
        'upload.error.expiredDuring': 'The link expired while uploading.',
        'upload.error.noLongerValid': 'The link is no longer valid.',
        'upload.error.serverRejected': 'The server rejected the upload. Please try again.',
        'upload.error.network': 'The upload failed. Check your connection and try again.',
        'upload.error.generic': 'Something went wrong.',
        'expiry.soon': 'in under an hour',
        'expiry.hours': 'in {count} hours',
        'expiry.days': 'in {count} days',

        'decrypt.title': 'Decrypt a transfer',
        'decrypt.intro': 'Opens a .bin file received through an upload link. Everything happens in this browser; the private key is never sent anywhere.',
        'decrypt.key.label': 'Private key (JWK)',
        'decrypt.key.load': 'Load key from file',
        'decrypt.key.loaded': 'Loaded {name}',
        'decrypt.key.required': 'Provide the private key first.',
        'decrypt.drop.title': 'Choose the encrypted file or drop it here',
        'decrypt.drop.hint': 'The downloaded .bin from your transfer bucket.',
        'decrypt.status.working': 'Decrypting...',
        'decrypt.status.done': 'Decrypted {name} ({size}).',
        'decrypt.save': 'Save {name}',
        'decrypt.footer': 'Need a key pair? Generate one at',
        'decrypt.error.wrongKey': 'Wrong key, or the file has been altered.',
        'decrypt.error.generic': 'Could not decrypt this file.',

        'keys.title': 'Transfer key pair',
        'keys.intro': 'Generates a P-256 key pair in this browser. The private key never leaves this page, so use this instead of letting the server generate one for you.',
        'keys.unsupported': 'This browser cannot generate keys here. Open this page over HTTPS (or on localhost).',
        'keys.generate': 'Generate a new key pair',
        'keys.generating': 'Generating...',
        'keys.warning': 'Save the private key now. It is the only thing that can open the files you receive, and nothing else holds a copy of it.',
        'keys.public.label': 'Public key - hand this to the link creator',
        'keys.public.copy': 'Copy public key',
        'keys.private.label': 'Private key (JWK) - keep this secret',
        'keys.private.download': 'Download private key',
        'keys.private.copy': 'Copy private key',
        'keys.curl.title': 'Create the upload link',
        'keys.curl.copy': 'Copy command',
        'keys.copied': 'Copied',
        'keys.failed': 'Key generation failed.',
        'keys.footer': 'To open the files later, go to',
        'keys.footer.suffix': 'and supply this private key.',

        'crypto.notEnvelope': 'This is not a StaticS3 transfer file.',
        'crypto.tooSmall': 'The file is too small to be an encrypted transfer.',
        'crypto.badVersion': 'Unsupported envelope version.',
        'crypto.badHeader': 'The envelope header is malformed.',
        'crypto.truncated': 'The file is truncated.',
        'crypto.corrupted': 'The file is corrupted.',
        'crypto.trailing': 'The file has unexpected trailing data.',
        'crypto.badMetadata': 'The file metadata is invalid.',
        'crypto.sizeMismatch': 'The decrypted size does not match the metadata.',
        'crypto.badPrivateKey': 'That is not a P-256 private key in JWK format.',
        'crypto.badPublicKey': 'The recipient public key is not a raw P-256 point.',

        'admin.title': 'Transfer admin console',
        'admin.intro': 'Cluster-internal only - reached through kubectl port-forward, never the public Ingress. Manage upload links, saved keys, and received uploads from here.',
        'admin.nav.console': 'Back to the admin console',
        'admin.nav.keys': 'Key pair generator',
        'admin.nav.decrypt': 'Decrypt a transfer',

        'admin.token.title': 'Admin token',
        'admin.token.description': 'Sent as X-Admin-Token on every request below. Kept only in this tab (sessionStorage) and forgotten when the tab closes.',
        'admin.token.label': 'X-Admin-Token',
        'admin.token.forget': 'Forget token',
        'admin.token.rejected': 'Admin token rejected.',

        'admin.create.title': 'Create an upload link',
        'admin.create.label': 'Label',
        'admin.create.ttl': 'Link validity',
        'admin.create.ttl.1h': '1 hour',
        'admin.create.ttl.24h': '24 hours',
        'admin.create.ttl.48h': '48 hours',
        'admin.create.ttl.7d': '7 days',
        'admin.create.ttl.14d': '14 days',
        'admin.create.maxBytes': 'Maximum upload size',
        'admin.create.maxBytes.10mb': '10 MB',
        'admin.create.maxBytes.100mb': '100 MB',
        'admin.create.maxBytes.256mb': '256 MB',
        'admin.create.keySource': 'Recipient key',
        'admin.create.keySource.generate': 'Generate a new key pair in this browser',
        'admin.create.keySource.reuse': 'Reuse a saved key',
        'admin.create.savedKey': 'Saved key',
        'admin.create.savedKey.empty': 'No saved keys yet',
        'admin.create.submit': 'Create link',
        'admin.create.submitting': 'Creating...',
        'admin.create.error': 'Could not create the link. Check the admin token and try again.',
        'admin.create.error.selectKey': 'Choose a saved key first.',
        'admin.create.result.title': 'Link created',
        'admin.create.result.url': 'Upload link',
        'admin.create.result.ticket': 'Ticket id',
        'admin.create.copy': 'Copy link',
        'admin.create.savedNotice': 'The new key pair has been saved in this browser - see "Saved keys" below.',

        'admin.keys.title': 'Saved keys (this browser)',
        'admin.keys.warning': "These private keys live only in this browser's local storage. Clearing site data destroys them permanently, and the server keeps no copy - download a backup of every key you cannot afford to lose. Losing a key makes its uploads permanently unrecoverable.",
        'admin.keys.unavailable': 'Local storage is unavailable in this window (e.g. private browsing), so keys cannot be saved here. Download the backup immediately after creating a link.',
        'admin.keys.empty': 'No saved keys yet.',
        'admin.keys.column.label': 'Label',
        'admin.keys.column.id': 'Ticket id',
        'admin.keys.column.created': 'Created',
        'admin.keys.download': 'Download backup',
        'admin.keys.forget': 'Forget',
        'admin.keys.forgetConfirm': 'Forgetting this key is permanent. Any files uploaded for it become unrecoverable unless you already downloaded a backup. Continue?',

        'admin.uploads.title': 'Received uploads',
        'admin.uploads.refresh': 'Refresh',
        'admin.uploads.filter.label': 'Filter by ticket id',
        'admin.uploads.filter.apply': 'Apply',
        'admin.uploads.empty': 'No uploads found.',
        'admin.uploads.loadError': 'Could not load uploads.',
        'admin.uploads.column.ticket': 'Ticket',
        'admin.uploads.column.file': 'File',
        'admin.uploads.column.size': 'Size',
        'admin.uploads.column.modified': 'Received',
        'admin.uploads.decrypt': 'Decrypt & save',
        'admin.uploads.download': 'Download ciphertext',
        'admin.uploads.noKeyHint': 'The key for this ticket is not saved in this browser. Download the ciphertext and open it with decrypt.html, pasting the matching private key there.',
        'admin.uploads.downloadError': 'Could not download the ciphertext.'
    },
    de: {
        'upload.title': 'Sicherer Datei-Upload',
        'upload.checking': 'Ihr Upload-Link wird geprüft ...',
        'upload.ready': 'Ihre Dateien werden in diesem Browser verschlüsselt, bevor sie Ihr Gerät verlassen.',
        'upload.error.noToken': 'Diesem Link fehlt das Upload-Token. Bitte verwenden Sie den vollständigen Link, den Sie erhalten haben.',
        'upload.error.unreachable': 'Der Server ist nicht erreichbar. Bitte prüfen Sie Ihre Verbindung und versuchen Sie es erneut.',
        'upload.error.expired': 'Dieser Upload-Link ist abgelaufen. Bitte fordern Sie einen neuen an.',
        'upload.error.invalid': 'Dieser Upload-Link ist ungültig. Bitte fordern Sie einen neuen an.',
        'upload.fact.for': 'Für',
        'upload.fact.expires': 'Link läuft ab',
        'upload.fact.max': 'Maximale Dateigröße',
        'upload.defaultLabel': 'Dateiübermittlung',
        'upload.drop.title': 'Dateien auswählen oder hier ablegen',
        'upload.drop.hint': 'Jede Datei wird auf diesem Gerät verschlüsselt, bevor sie gesendet wird.',
        'upload.footer': 'Dateien werden mit dem öffentlichen Schlüssel des Empfängers per ECDH P-256 und AES-256-GCM versiegelt. Der Server speichert ausschließlich den Chiffretext und erhält niemals einen Schlüssel, der ihn öffnen kann.',
        'upload.status.encrypting': 'Wird auf diesem Gerät verschlüsselt ...',
        'upload.status.uploading': 'Wird hochgeladen ...',
        'upload.status.done': 'Verschlüsselt und übermittelt.',
        'upload.status.receipt': 'Beleg {hash} - {size} gespeichert',
        'upload.error.tooLarge': 'Zu groß. Dieser Link erlaubt maximal {size}.',
        'upload.error.tooLargeSealed': 'Nach der Verschlüsselung zu groß. Dieser Link erlaubt maximal {size}.',
        'upload.error.rejectedSize': 'Die Datei ist größer, als dieser Link erlaubt.',
        'upload.error.expiredDuring': 'Der Link ist während des Uploads abgelaufen.',
        'upload.error.noLongerValid': 'Der Link ist nicht mehr gültig.',
        'upload.error.serverRejected': 'Der Server hat den Upload abgelehnt. Bitte versuchen Sie es erneut.',
        'upload.error.network': 'Der Upload ist fehlgeschlagen. Bitte prüfen Sie Ihre Verbindung und versuchen Sie es erneut.',
        'upload.error.generic': 'Es ist ein Fehler aufgetreten.',
        'expiry.soon': 'in weniger als einer Stunde',
        'expiry.hours': 'in {count} Stunden',
        'expiry.days': 'in {count} Tagen',

        'decrypt.title': 'Übermittlung entschlüsseln',
        'decrypt.intro': 'Öffnet eine .bin-Datei, die über einen Upload-Link empfangen wurde. Alles geschieht in diesem Browser; der private Schlüssel wird niemals übertragen.',
        'decrypt.key.label': 'Privater Schlüssel (JWK)',
        'decrypt.key.load': 'Schlüssel aus Datei laden',
        'decrypt.key.loaded': '{name} geladen',
        'decrypt.key.required': 'Bitte geben Sie zuerst den privaten Schlüssel an.',
        'decrypt.drop.title': 'Verschlüsselte Datei auswählen oder hier ablegen',
        'decrypt.drop.hint': 'Die heruntergeladene .bin-Datei aus Ihrem Transfer-Bucket.',
        'decrypt.status.working': 'Wird entschlüsselt ...',
        'decrypt.status.done': '{name} entschlüsselt ({size}).',
        'decrypt.save': '{name} speichern',
        'decrypt.footer': 'Sie brauchen ein Schlüsselpaar? Erzeugen Sie eines unter',
        'decrypt.error.wrongKey': 'Falscher Schlüssel, oder die Datei wurde verändert.',
        'decrypt.error.generic': 'Diese Datei konnte nicht entschlüsselt werden.',

        'keys.title': 'Transfer-Schlüsselpaar',
        'keys.intro': 'Erzeugt ein P-256-Schlüsselpaar in diesem Browser. Der private Schlüssel verlässt diese Seite nie - nutzen Sie das, statt den Server ein Schlüsselpaar erzeugen zu lassen.',
        'keys.unsupported': 'Dieser Browser kann hier keine Schlüssel erzeugen. Öffnen Sie diese Seite über HTTPS (oder auf localhost).',
        'keys.generate': 'Neues Schlüsselpaar erzeugen',
        'keys.generating': 'Wird erzeugt ...',
        'keys.warning': 'Sichern Sie den privaten Schlüssel jetzt. Er ist das Einzige, womit sich die empfangenen Dateien öffnen lassen, und es existiert keine weitere Kopie davon.',
        'keys.public.label': 'Öffentlicher Schlüssel - geben Sie diesen an die Person weiter, die den Link erstellt',
        'keys.public.copy': 'Öffentlichen Schlüssel kopieren',
        'keys.private.label': 'Privater Schlüssel (JWK) - geheim halten',
        'keys.private.download': 'Privaten Schlüssel herunterladen',
        'keys.private.copy': 'Privaten Schlüssel kopieren',
        'keys.curl.title': 'Upload-Link erstellen',
        'keys.curl.copy': 'Befehl kopieren',
        'keys.copied': 'Kopiert',
        'keys.failed': 'Schlüsselerzeugung fehlgeschlagen.',
        'keys.footer': 'Um die Dateien später zu öffnen, gehen Sie zu',
        'keys.footer.suffix': 'und geben Sie diesen privaten Schlüssel an.',

        'crypto.notEnvelope': 'Dies ist keine StaticS3-Transferdatei.',
        'crypto.tooSmall': 'Die Datei ist zu klein für eine verschlüsselte Übermittlung.',
        'crypto.badVersion': 'Nicht unterstützte Envelope-Version.',
        'crypto.badHeader': 'Der Envelope-Header ist fehlerhaft.',
        'crypto.truncated': 'Die Datei ist unvollständig.',
        'crypto.corrupted': 'Die Datei ist beschädigt.',
        'crypto.trailing': 'Die Datei enthält unerwartete zusätzliche Daten.',
        'crypto.badMetadata': 'Die Metadaten der Datei sind ungültig.',
        'crypto.sizeMismatch': 'Die entschlüsselte Größe stimmt nicht mit den Metadaten überein.',
        'crypto.badPrivateKey': 'Das ist kein privater P-256-Schlüssel im JWK-Format.',
        'crypto.badPublicKey': 'Der öffentliche Schlüssel des Empfängers ist kein roher P-256-Punkt.',

        'admin.title': 'Transfer-Admin-Konsole',
        'admin.intro': 'Nur clusterintern - erreichbar per kubectl port-forward, niemals über das öffentliche Ingress. Verwalten Sie hier Upload-Links, gespeicherte Schlüssel und eingegangene Uploads.',
        'admin.nav.console': 'Zurück zur Admin-Konsole',
        'admin.nav.keys': 'Schlüsselpaar-Generator',
        'admin.nav.decrypt': 'Übermittlung entschlüsseln',

        'admin.token.title': 'Admin-Token',
        'admin.token.description': 'Wird bei jeder Anfrage unten als X-Admin-Token gesendet. Es wird nur in diesem Tab (sessionStorage) gehalten und beim Schließen des Tabs vergessen.',
        'admin.token.label': 'X-Admin-Token',
        'admin.token.forget': 'Token vergessen',
        'admin.token.rejected': 'Admin-Token wurde abgelehnt.',

        'admin.create.title': 'Upload-Link erstellen',
        'admin.create.label': 'Bezeichnung',
        'admin.create.ttl': 'Gültigkeit des Links',
        'admin.create.ttl.1h': '1 Stunde',
        'admin.create.ttl.24h': '24 Stunden',
        'admin.create.ttl.48h': '48 Stunden',
        'admin.create.ttl.7d': '7 Tage',
        'admin.create.ttl.14d': '14 Tage',
        'admin.create.maxBytes': 'Maximale Upload-Größe',
        'admin.create.maxBytes.10mb': '10 MB',
        'admin.create.maxBytes.100mb': '100 MB',
        'admin.create.maxBytes.256mb': '256 MB',
        'admin.create.keySource': 'Schlüssel des Empfängers',
        'admin.create.keySource.generate': 'Neues Schlüsselpaar in diesem Browser erzeugen',
        'admin.create.keySource.reuse': 'Gespeicherten Schlüssel wiederverwenden',
        'admin.create.savedKey': 'Gespeicherter Schlüssel',
        'admin.create.savedKey.empty': 'Noch keine gespeicherten Schlüssel',
        'admin.create.submit': 'Link erstellen',
        'admin.create.submitting': 'Wird erstellt ...',
        'admin.create.error': 'Der Link konnte nicht erstellt werden. Bitte prüfen Sie das Admin-Token und versuchen Sie es erneut.',
        'admin.create.error.selectKey': 'Bitte wählen Sie zuerst einen gespeicherten Schlüssel aus.',
        'admin.create.result.title': 'Link erstellt',
        'admin.create.result.url': 'Upload-Link',
        'admin.create.result.ticket': 'Ticket-ID',
        'admin.create.copy': 'Link kopieren',
        'admin.create.savedNotice': 'Das neue Schlüsselpaar wurde in diesem Browser gespeichert - siehe "Gespeicherte Schlüssel" unten.',

        'admin.keys.title': 'Gespeicherte Schlüssel (dieser Browser)',
        'admin.keys.warning': 'Diese privaten Schlüssel liegen ausschließlich im lokalen Speicher dieses Browsers. Das Löschen von Website-Daten vernichtet sie unwiderruflich, und der Server besitzt keine Kopie - laden Sie für jeden unverzichtbaren Schlüssel eine Sicherungskopie herunter. Der Verlust eines Schlüssels macht die zugehörigen Uploads dauerhaft unwiederbringlich.',
        'admin.keys.unavailable': 'Der lokale Speicher ist in diesem Fenster nicht verfügbar (z. B. privates Fenster), Schlüssel können hier also nicht gespeichert werden. Laden Sie die Sicherungskopie sofort nach dem Erstellen eines Links herunter.',
        'admin.keys.empty': 'Noch keine gespeicherten Schlüssel.',
        'admin.keys.column.label': 'Bezeichnung',
        'admin.keys.column.id': 'Ticket-ID',
        'admin.keys.column.created': 'Erstellt',
        'admin.keys.download': 'Sicherungskopie herunterladen',
        'admin.keys.forget': 'Vergessen',
        'admin.keys.forgetConfirm': 'Das Vergessen dieses Schlüssels ist endgültig. Ohne bereits heruntergeladene Sicherungskopie werden die zugehörigen Dateien unwiederbringlich. Fortfahren?',

        'admin.uploads.title': 'Eingegangene Uploads',
        'admin.uploads.refresh': 'Aktualisieren',
        'admin.uploads.filter.label': 'Nach Ticket-ID filtern',
        'admin.uploads.filter.apply': 'Anwenden',
        'admin.uploads.empty': 'Keine Uploads gefunden.',
        'admin.uploads.loadError': 'Uploads konnten nicht geladen werden.',
        'admin.uploads.column.ticket': 'Ticket',
        'admin.uploads.column.file': 'Datei',
        'admin.uploads.column.size': 'Größe',
        'admin.uploads.column.modified': 'Empfangen',
        'admin.uploads.decrypt': 'Entschlüsseln & speichern',
        'admin.uploads.download': 'Chiffretext herunterladen',
        'admin.uploads.noKeyHint': 'Der Schlüssel für dieses Ticket ist in diesem Browser nicht gespeichert. Laden Sie den Chiffretext herunter und öffnen Sie ihn mit decrypt.html, indem Sie dort den passenden privaten Schlüssel einfügen.',
        'admin.uploads.downloadError': 'Der Chiffretext konnte nicht heruntergeladen werden.'
    }
};

export const SUPPORTED = ['de', 'en'];

// Accepts a single tag or an ordered preference list, and returns the first supported
// language. Order matters: ['en-US', 'de'] means the reader prefers English.
export function resolveLanguage(tags) {
    for (const tag of Array.isArray(tags) ? tags : [tags]) {
        const base = String(tag || '').toLowerCase().split('-')[0];
        if (SUPPORTED.includes(base)) return base;
    }
    return 'en';
}

// navigator.language alone is unreliable: in Firefox it mirrors the *content* language
// (intl.accept_languages), so a German-UI browser configured to request en-US reads as
// English. Prefer the full navigator.languages list, and let ?lang= force it outright.
export function detectLanguage(navigatorLike = globalThis.navigator, search = globalThis.location?.search) {
    const override = new URLSearchParams(search || '').get('lang');
    if (override) return resolveLanguage(override);
    const preferences = navigatorLike?.languages;
    return resolveLanguage(preferences?.length ? [...preferences] : navigatorLike?.language);
}

export const language = detectLanguage();
export const locale = language === 'de' ? 'de-DE' : 'en-US';

export function t(key, params) {
    const template = DICTIONARIES[language][key] ?? DICTIONARIES.en[key] ?? key;
    if (!params) return template;
    return template.replace(/\{(\w+)\}/g, (match, name) => (name in params ? String(params[name]) : match));
}

export function formatSize(bytes) {
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit++;
    }
    const digits = unit === 0 ? 0 : value < 10 ? 1 : 0;
    return `${value.toLocaleString(locale, { minimumFractionDigits: digits, maximumFractionDigits: digits })} ${units[unit]}`;
}

export function formatExpiry(iso) {
    const at = new Date(iso);
    const hours = Math.round((at - Date.now()) / 3600000);
    const relative = hours < 1
        ? t('expiry.soon')
        : hours < 48
            ? t('expiry.hours', { count: hours })
            : t('expiry.days', { count: Math.round(hours / 24) });
    return `${at.toLocaleString(locale)} (${relative})`;
}

// Translates [data-i18n] text nodes and the document title, and stamps <html lang>.
export function applyTranslations(root = document) {
    if (typeof document !== 'undefined')
        document.documentElement.lang = language;
    for (const element of root.querySelectorAll('[data-i18n]'))
        element.textContent = t(element.dataset.i18n);
    for (const element of root.querySelectorAll('[data-i18n-title]'))
        document.title = t(element.dataset.i18nTitle);
}
