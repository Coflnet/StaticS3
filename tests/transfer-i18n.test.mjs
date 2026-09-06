// Guards against a half-translated UI: every key must exist in both dictionaries with a
// non-empty value, every {placeholder} used on one side must appear on the other, and
// resolveLanguage() must keep routing German variants to 'de' and everything else to 'en'.
// Run with: node --test tests/
import test from 'node:test';
import assert from 'node:assert/strict';

import { DICTIONARIES, resolveLanguage } from '../wwwroot/transfer/i18n.js';

const placeholders = (value) => [...value.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort();

test('en and de dictionaries have identical key sets', () => {
    const enKeys = new Set(Object.keys(DICTIONARIES.en));
    const deKeys = new Set(Object.keys(DICTIONARIES.de));

    const missingFromDe = [...enKeys].filter((key) => !deKeys.has(key));
    const missingFromEn = [...deKeys].filter((key) => !enKeys.has(key));

    assert.deepEqual(missingFromDe, [], `keys missing from de: ${missingFromDe.join(', ')}`);
    assert.deepEqual(missingFromEn, [], `keys missing from en: ${missingFromEn.join(', ')}`);
});

test('no dictionary value is empty', () => {
    for (const [language, dictionary] of Object.entries(DICTIONARIES)) {
        for (const [key, value] of Object.entries(dictionary))
            assert.ok(typeof value === 'string' && value.trim().length > 0, `${language}.${key} is empty`);
    }
});

test('every {placeholder} used in en appears in de and vice versa', () => {
    const mismatches = [];
    for (const key of Object.keys(DICTIONARIES.en)) {
        if (!(key in DICTIONARIES.de)) continue; // reported by the key-set test above
        const enParams = placeholders(DICTIONARIES.en[key]);
        const deParams = placeholders(DICTIONARIES.de[key]);
        if (enParams.join(',') !== deParams.join(','))
            mismatches.push(`${key}: en=[${enParams}] de=[${deParams}]`);
    }
    assert.deepEqual(mismatches, [], `placeholder mismatches:\n${mismatches.join('\n')}`);
});

test('resolveLanguage maps German variants to de', () => {
    assert.equal(resolveLanguage('de'), 'de');
    assert.equal(resolveLanguage('de-DE'), 'de');
    assert.equal(resolveLanguage('de-AT'), 'de');
});

test('resolveLanguage falls back to en for everything else', () => {
    assert.equal(resolveLanguage('en-US'), 'en');
    assert.equal(resolveLanguage('fr'), 'en');
    assert.equal(resolveLanguage(undefined), 'en');
});

// Guards the failure mode where a page references a key nobody added to the dictionaries:
// applyTranslations() would silently render the raw key name to the customer.
test('every data-i18n key used in the pages exists in both dictionaries', async () => {
    const { readFileSync, readdirSync } = await import('node:fs');
    const root = new URL('../wwwroot/', import.meta.url);
    const pages = [];
    for (const dir of ['transfer', 'transfer-admin'])
        for (const name of readdirSync(new URL(`${dir}/`, root)))
            if (name.endsWith('.html')) pages.push(`${dir}/${name}`);

    assert.ok(pages.length >= 4, 'expected the transfer and transfer-admin pages');
    for (const page of pages) {
        const html = readFileSync(new URL(page, root), 'utf8');
        for (const [, key] of html.matchAll(/data-i18n(?:-title)?="([^"]+)"/g)) {
            assert.ok(key in DICTIONARIES.en, `${page} uses unknown key ${key}`);
            assert.ok(key in DICTIONARIES.de, `${page} uses key ${key} missing from de`);
        }
    }
});
