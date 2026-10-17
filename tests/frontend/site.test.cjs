const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../src/BelemRestaurantes.Web/wwwroot/js/site.js'), 'utf8');
class Element {
  constructor(tag = '') { this.tag = tag; this.children = []; this.textContent = ''; this.value = ''; this.events = {}; }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = nodes; }
  add(node) { this.children.push(node); }
  addEventListener(event, callback) { this.events[event] = callback; }
}
async function render(result) {
  const ids = Object.fromEntries(['go', 'query', 'city', 'status', 'cards', 'destination', 'autosuggest', 'client-text'].map(id => [id, new Element(id)]));
  ids['client-text'].dataset = { loading: 'Loading', unavailable: 'Unavailable', results: 'Found {0}', choose: 'Choose', photo: 'Photo {0}', noPhoto: 'No photo', address: 'Checking', website: 'Website' };
  const document = { documentElement: { lang: 'en' }, getElementById: id => ids[id], addEventListener() {}, createElement: tag => new Element(tag), createTextNode: text => ({ textContent: text }) };
  class Option extends Element { constructor(text, value) { super('option'); this.textContent = text; this.value = value; } }
  vm.runInNewContext(source, { document, URL, URLSearchParams, AbortController, Intl, Option, HTMLFormElement: Element,
    fetch: async () => ({ ok: true, json: async () => result }), setTimeout, clearTimeout });
  await new Promise(resolve => setImmediate(resolve));
  return ids;
}
test('catalogue reads paginated items, keeps user data as text and links to the real restaurant route', async () => {
  const ids = await render({ items: [{ id: 7, name: '<script>untrusted</script>', city: 'Belém', website: 'javascript:alert(1)' }], totalCount: 1 });
  assert.equal(ids.status.textContent, 'Found 1');
  const content = ids.cards.children[0].children[1];
  assert.equal(content.children[0].children[0].href, '/establishment/7');
  assert.equal(content.children[0].children[0].textContent, '<script>untrusted</script>');
  assert.equal(content.children.filter(node => node.tag === 'a').length, 0);
});
test('invalid catalogue response shows the localized error and clears suggestions', async () => {
  const ids = await render({ unexpected: [] });
  assert.equal(ids.status.textContent, 'Unavailable');
  assert.equal(ids.cards.children.length, 0);
  assert.equal(ids.autosuggest.children.length, 0);
});
