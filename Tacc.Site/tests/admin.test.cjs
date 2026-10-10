const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sessionKey = 'tacc.admin.session.v1';
const now = Date.parse('2026-10-10T12:00:00Z');
const inventory = { productId: 'tacc-shirt', name: 'TACC Shirt', etag: '"1"', variants: [
  { variantId: 'S', quantity: 4 }, { variantId: 'XXL', quantity: 7 }
] };
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(setImmediate); };

// A small DOM/fetch harness drives the actual production script. It makes no network calls.
class Element {
  constructor() {
    this.hidden = false; this.disabled = false; this.value = ''; this.textContent = '';
    this.listeners = new Map(); this.children = [];
    this.classList = { add() {}, remove() {} };
  }
  addEventListener(name, callback) { this.listeners.set(name, callback); }
  emit(name) { this.listeners.get(name)?.({ preventDefault() {}, returnValue: undefined }); }
  append(...elements) { this.children.push(...elements); }
  replaceChildren(...elements) { this.children = elements; }
  setAttribute(name, value) { this[name] = value; }
  setCustomValidity(value) { this.validation = value; }
  checkValidity() { return !this.validation; }
  reportValidity() { return this.checkValidity(); }
}

async function harness(filename, { stored, responder, storageDisabled = false } = {}) {
  const html = fs.readFileSync(path.join(__dirname, '../wwwroot/admin/index.html'), 'utf8');
  const elements = new Map();
  for (const match of html.matchAll(/\bdata-([a-z-]+)(?=[\s=>])/g)) elements.set(match[1], new Element());
  const views = [...html.matchAll(/data-view="([a-z-]+)"/g)].map(match => {
    const view = new Element(); view.dataset = { view: match[1] }; return view;
  });
  const storage = new Map(stored ? [[sessionKey, JSON.stringify(stored)]] : []);
  const calls = []; const timers = new Map(); const events = new Map(); let time = now; let timerId = 0;
  const window = {
    location: { hostname: 'localhost', protocol: 'http:', href: 'http://localhost:7000/admin/' },
    TACC_CONFIG: { apiBaseUrl: 'http://localhost:7071/api', admin: { productId: 'tacc-shirt' } },
    sessionStorage: {
      getItem: key => storage.get(key) ?? null,
      setItem: (key, value) => { if (storageDisabled) throw new Error('Disabled'); storage.set(key, value); },
      removeItem: key => storage.delete(key)
    },
    setTimeout: (callback, delay) => { const id = ++timerId; timers.set(id, { callback, delay }); return id; },
    clearTimeout: id => timers.delete(id),
    addEventListener: (name, callback) => events.set(name, callback),
    confirm: () => true
  };
  const document = {
    querySelector: selector => elements.get(selector.match(/\[data-([^\]]+)\]/)?.[1]),
    querySelectorAll: selector => selector === '[data-view]' ? views : [elements.get(selector.match(/\[data-([^\]]+)\]/)?.[1])].filter(Boolean),
    createElement: () => new Element()
  };
  const fetch = async (url, options) => {
    calls.push({ url, options });
    const result = responder ? await responder(url, options, calls.length) : { status: 200, payload: inventory };
    return { status: result.status, ok: result.status >= 200 && result.status < 300, json: async () => result.payload };
  };
  class ClockDate extends Date { static now() { return time; } }
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../wwwroot/assets/js', filename), 'utf8'), {
    window, document, fetch, Date: ClockDate, URL, AbortController, console: { error() {} }
  });
  await flush();
  return {
    elements, calls, storage, views, window,
    visible: () => views.find(view => !view.hidden)?.dataset.view,
    login: async () => {
      elements.get('username').value = 'owner'; elements.get('password').value = 'Synthetic password only!';
      elements.get('login-form').emit('submit'); await flush();
    },
    expire: () => {
      time += 8 * 60 * 60 * 1000;
      for (const timer of [...timers.values()]) if (timer.delay > 10000) timer.callback();
      events.get('focus')?.();
    }
  };
}

for (const filename of ['admin.js', 'admin.bundle.js']) {
  test(`${filename}: login stores session, sends bearer and preserves quantity/ETag contract`, async () => {
    const h = await harness(filename, { responder: async (url, options) => {
      if (url.endsWith('/admin/login')) return { status: 200, payload: { accessToken: 'synthetic-token', expiresAt: new Date(now + 28800000).toISOString() } };
      if (options.method === 'PUT') return { status: 200, payload: { ...inventory, etag: '"2"' } };
      return { status: 200, payload: inventory };
    } });
    assert.equal(h.visible(), 'signed-out');
    await h.login();
    assert.equal(h.visible(), 'authorized');
    assert.equal(h.elements.get('password').value, '');
    assert.equal(JSON.parse(h.storage.get(sessionKey)).accessToken, 'synthetic-token');
    assert.equal(h.calls[0].options.credentials, 'omit');
    assert.equal(h.calls[0].options.redirect, 'error');
    assert.equal(h.calls[1].options.headers.Authorization, 'Bearer synthetic-token');
    assert.equal(h.elements.get('variant-list').children.length, 2);
    const row = h.elements.get('variant-list').children[1];
    assert.equal(row.children[0].children[0].textContent, 'XXL');
    row.children[1].value = '9';
    h.elements.get('inventory-form').emit('submit'); await flush();
    const put = h.calls.find(call => call.options.method === 'PUT');
    assert.deepEqual(JSON.parse(put.options.body), { etag: '"1"', variants: { S: 4, XXL: 9 } });
    assert.equal(put.options.headers.Authorization, 'Bearer synthetic-token');
    h.elements.get('sign-out').emit('click');
    assert.equal(h.visible(), 'signed-out'); assert.equal(h.storage.has(sessionKey), false);
  });

  test(`${filename}: restores valid sessions and clears expired sessions`, async () => {
    const stored = { accessToken: 'synthetic-token', username: 'owner', expiresAt: now + 28800000 };
    const h = await harness(filename, { stored });
    assert.equal(h.visible(), 'authorized'); assert.equal(h.calls.length, 1);
    h.expire();
    assert.equal(h.visible(), 'signed-out'); assert.equal(h.storage.has(sessionKey), false);
    const expired = await harness(filename, { stored: { ...stored, expiresAt: now } });
    assert.equal(expired.visible(), 'signed-out'); assert.equal(expired.calls.length, 0);
  });

  test(`${filename}: API 401 clears session without token refresh`, async () => {
    const h = await harness(filename, { stored: { accessToken: 'synthetic', username: 'owner', expiresAt: now + 100000 }, responder: async () => ({ status: 401 }) });
    assert.equal(h.visible(), 'signed-out'); assert.equal(h.storage.has(sessionKey), false); assert.equal(h.calls.length, 1);
  });

  test(`${filename}: logout ignores an inventory request still in flight`, async () => {
    let finish;
    const pending = new Promise(resolve => { finish = resolve; });
    const h = await harness(filename, { stored: { accessToken: 'synthetic', username: 'owner', expiresAt: now + 100000 }, responder: async () => pending });
    h.elements.get('sign-out').emit('click');
    finish({ status: 200, payload: inventory }); await flush();
    assert.equal(h.visible(), 'signed-out'); assert.equal(h.storage.has(sessionKey), false);
    assert.equal(h.elements.get('variant-list').children.length, 0);
  });

  test(`${filename}: conflict keeps editor and exposes reload action`, async () => {
    const h = await harness(filename, { stored: { accessToken: 'synthetic', username: 'owner', expiresAt: now + 100000 }, responder: async (_, options) => ({ status: options.method === 'PUT' ? 409 : 200, payload: inventory }) });
    h.elements.get('inventory-form').emit('submit'); await flush();
    assert.equal(h.visible(), 'authorized'); assert.equal(h.elements.get('conflict-reload').hidden, false);
    assert.match(h.elements.get('inventory-status').textContent, /Inventory changed/);
  });

  test(`${filename}: failed logins clear passwords and show safe messages`, async () => {
    for (const status of [400, 401, 429, 503]) {
      const h = await harness(filename, { responder: async () => ({ status }) });
      await h.login();
      assert.equal(h.visible(), 'signed-out'); assert.equal(h.elements.get('password').value, '');
      assert.equal(h.elements.get('auth-error').hidden, false); assert.equal(h.storage.size, 0);
      assert.equal(h.elements.get('sign-in').disabled, false);
    }
  });

  test(`${filename}: invalid quantities never send a PUT`, async () => {
    const h = await harness(filename, { stored: { accessToken: 'synthetic', username: 'owner', expiresAt: now + 100000 } });
    h.elements.get('variant-list').children[0].children[1].value = '-1';
    h.elements.get('inventory-form').emit('submit'); await flush();
    assert.equal(h.calls.some(call => call.options.method === 'PUT'), false);
  });

  test(`${filename}: storage failure and network failure never retain credentials`, async () => {
    const blocked = await harness(filename, { storageDisabled: true, responder: async () => ({ status: 200, payload: { accessToken: 'synthetic', expiresAt: new Date(now + 100000).toISOString() } }) });
    await blocked.login(); assert.equal(blocked.visible(), 'signed-out'); assert.equal(blocked.storage.size, 0);
    assert.match(blocked.elements.get('auth-error').textContent, /session storage/);
    const failed = await harness(filename, { responder: async () => { throw new Error('Network failure'); } });
    await failed.login(); assert.equal(failed.elements.get('password').value, ''); assert.equal(failed.storage.size, 0);
  });
}
