const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../src/BelemRestaurantes.Web/wwwroot/js/account.js'), 'utf8');
function execute(fragment, current = '') {
  const field = { value: current }, button = { disabled: true }, history = [];
  const form = { querySelector: selector => selector === '[data-account-token]' ? field : button };
  vm.runInNewContext(source, {
    document: { querySelectorAll: () => [form] }, URLSearchParams,
    window: { location: { hash: fragment, pathname: '/account/reset', search: '' }, history: { replaceState: (...args) => history.push(args) } }
  });
  return { field, button, history };
}
test('recovery token moves from fragment to POST input and is removed from visible URL', () => {
  const token = 'A'.repeat(64), result = execute('#token=' + token);
  assert.equal(result.field.value, token);
  assert.equal(result.button.disabled, false);
  assert.equal(result.history[0][2], '/account/reset');
  assert.equal(result.history[0].some(value => String(value).includes(token)), false);
});
test('malformed fragments cannot enable confirmation and a valid POST token survives validation errors', () => {
  const invalid = execute('#token=%3Cscript%3E');
  assert.equal(invalid.button.disabled, true);
  assert.equal(invalid.field.value, '');
  assert.equal(execute('', 'B'.repeat(64)).button.disabled, false);
});
