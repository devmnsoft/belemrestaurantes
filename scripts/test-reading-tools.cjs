// Dependency-free behavior checks. Browser rendering is a separate validation step.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const code = fs.readFileSync('src/BelemRestaurantes.Web/wwwroot/js/accessibility.js', 'utf8');
function fixture(initial, unavailable = false) {
  const classes = new Set(); const elements = new Map(); let saved = initial;
  function element(selector) {
    if (!elements.has(selector)) elements.set(selector, { checked: false, value: '', textContent: '', handlers: {}, addEventListener(event, fn) { this.handlers[event] = fn; } });
    return elements.get(selector);
  }
  const root = { dataset: {}, classList: { toggle(key, on) { on ? classes.add(key) : classes.delete(key); } } };
  const tools = { querySelector: element };
  vm.runInNewContext(code, { document: { documentElement: root, querySelector: () => tools }, localStorage: {
    getItem() { if (unavailable) throw new Error('disabled'); return saved; },
    setItem(key, value) { if (unavailable) throw new Error('disabled'); saved = value; }
  } });
  return { root, classes, element, saved: () => saved };
}
let page = fixture(JSON.stringify({ size: '150', contrast: true, links: true, motion: true }));
assert.equal(page.root.dataset.textSize, '150');
assert.ok(page.classes.has('reading-contrast'));
assert.ok(page.classes.has('reading-motion'));
page.element('[data-reading-reset]').handlers.click();
assert.equal(page.root.dataset.textSize, '100');
assert.equal(page.classes.size, 0);
page.element('[data-reading-size]').value = '125';
page.element('[data-reading-size]').handlers.change();
assert.equal(page.root.dataset.textSize, '125');
page.element('[data-reading-contrast]').handlers.change({ target: { checked: true } });
assert.ok(page.classes.has('reading-contrast'));
assert.ok(page.element('[data-reading-status]').textContent.includes('atualizadas'));
page = fixture(page.saved());
assert.equal(page.root.dataset.textSize, '125');
assert.ok(page.classes.has('reading-contrast'));
assert.equal(fixture('{broken').root.dataset.textSize, '100');
assert.equal(fixture(null, true).root.dataset.textSize, '100');
assert.equal(fixture(JSON.stringify({ size: '900', contrast: 'true' })).root.dataset.textSize, '100');
console.log('Reading tools: persistence, reset, contrast, size, announcements and storage failures passed.');
