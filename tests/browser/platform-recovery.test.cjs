const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../src/Neftyanik.Portal.Web/wwwroot/js/platform-recovery.js'), 'utf8');

function execute(hash, formPresent = true) {
    const fields = { UserId: { value: 'existing-user' }, Token: { value: 'existing-token' } };
    const location = { hash, pathname: '/Platform/Account/ResetPassword' };
    const replacements = [];
    vm.runInNewContext(source, {
        URLSearchParams,
        document: { querySelector: selector => {
            assert.equal(selector, '[data-recovery-form]');
            return formPresent ? { elements: fields } : null;
        } },
        window: { location, history: { replaceState: (state, title, url) => {
            assert.equal(state, null);
            assert.equal(title, '');
            replacements.push(url);
            location.hash = '';
        } } }
    });
    return { fields, location, replacements };
}

test('fragment token is decoded exactly once and removed from the current history entry', () => {
    const token = 'base64+/=and%2B';
    const result = execute('#userId=' + encodeURIComponent('user+123') + '&token=' + encodeURIComponent(token));
    assert.equal(result.fields.UserId.value, 'user+123');
    assert.equal(result.fields.Token.value, token);
    assert.equal(result.location.hash, '');
    assert.deepEqual(result.replacements, ['/Platform/Account/ResetPassword']);
});

test('ordinary GET or failed POST without a fragment preserves server-rendered fields', () => {
    const result = execute('');
    assert.equal(result.fields.Token.value, 'existing-token');
    assert.equal(result.fields.UserId.value, 'existing-user');
    assert.deepEqual(result.replacements, []);
});

test('incomplete links clear missing credentials instead of keeping stale fields', () => {
    const result = execute('#unrelated=value');
    assert.equal(result.fields.Token.value, '');
    assert.equal(result.fields.UserId.value, '');
    assert.equal(result.location.hash, '');
});

test('fragment cannot select a redirect target or another history URL', () => {
    const result = execute('#userId=u&token=t&returnUrl=https%3A%2F%2Fattacker.example');
    assert.deepEqual(result.replacements, ['/Platform/Account/ResetPassword']);
});

test('script is safe when no recovery form exists', () => {
    const result = execute('#token=t', false);
    assert.deepEqual(result.replacements, []);
});
