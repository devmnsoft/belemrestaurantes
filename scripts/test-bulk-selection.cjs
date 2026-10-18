const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const code = fs.readFileSync('src/BelemRestaurantes.Web/wwwroot/js/bulk-moderation.js', 'utf8');
const storage = new Map();
function element(extra = {}) {
  return Object.assign({ handlers: {}, checked: false, disabled: false, textContent: '', hidden: true,
    addEventListener(type, fn) { this.handlers[type] = fn; }, setAttribute() {}, removeAttribute() {} }, extra);
}
function page(ids, key = 'filter-a', limit = 2, unavailable = false) {
  const items = ids.map(value => element({ value, closest: () => ({ classList: { toggle() {} } }) }));
  const all = element(); const count = element(); const submit = element(); const note = element();
  const hidden = { children: [], replaceChildren() { this.children = []; }, appendChild(el) { this.children.push(el); } };
  const controls = { 'input[name=allFiltered]': all, '[data-bulk-count]': count, '[data-bulk-hidden]': hidden,
    '[data-bulk-persist-note]': note, 'button[type=submit]': submit, '[data-bulk-page]': element(), '[data-bulk-clear]': element() };
  const form = element({ dataset: { selectionKey: key, batchLimit: String(limit), filterCount: '2' }, querySelectorAll: () => items, querySelector: selector => controls[selector] });
  const window = element();
  vm.runInNewContext(code, { document: { querySelector: selector => selector === '[data-bulk-form]' ? form : null,
    querySelectorAll: () => [], createElement: () => ({}) }, window,
    sessionStorage: { getItem(k) { if (unavailable) throw Error('blocked'); return storage.get(k) || null; },
      setItem(k, v) { if (unavailable) throw Error('blocked'); storage.set(k, v); } } });
  return { items, all, count, submit, hidden, note, controls, form, window };
}
let first = page(['1']);
assert.equal(first.submit.disabled, true);
first.items[0].checked = true; first.items[0].handlers.change();
assert.ok(first.count.textContent.startsWith('1 selecionados'));
let next = page(['2']);
assert.equal(next.hidden.children[0].value, '1');
assert.ok(next.count.textContent.includes('1 em outras páginas'));
next.items[0].checked = true; next.items[0].handlers.change();
let third = page(['3']);
third.items[0].checked = true; third.items[0].handlers.change();
assert.equal(third.items[0].checked, false);
assert.ok(third.count.textContent.includes('Limite de 2'));
third.all.checked = true; third.all.handlers.change();
assert.equal(third.items[0].disabled, true);
assert.equal(third.hidden.children.length, 0);
third.controls['[data-bulk-clear]'].handlers.click();
assert.equal(third.submit.disabled, true);
assert.equal(third.items[0].disabled, false);
third.controls['[data-bulk-page]'].handlers.click();
assert.equal(third.items[0].checked, true);
assert.equal(page(['3'], 'different-filter').items[0].checked, false);
third.form.handlers.submit(); assert.equal(third.submit.disabled, true);
third.window.handlers.pageshow(); assert.equal(third.submit.disabled, false);
const broken = page(['4'], 'blocked', 2, true);
assert.ok(broken.note.textContent.includes('não permitiu'));
storage.set('belem-bulk:filter-a', JSON.stringify({ ids: ['3'], all: false }));
vm.runInNewContext(code, { document: { querySelector: selector => selector === '[data-bulk-prepared-key]' ? { dataset: { bulkPreparedKey: 'filter-a' } } : null, querySelectorAll: () => [] }, sessionStorage: { removeItem: key => storage.delete(key) } });
assert.equal(storage.has('belem-bulk:filter-a'), false);
third.window.handlers.pageshow();
assert.equal(third.submit.disabled, true);
assert.equal(third.items[0].checked, false);
console.log('Bulk selection: multiple pages, hidden IDs, limit, filters, reset, preparation, back navigation and storage failures passed.');
