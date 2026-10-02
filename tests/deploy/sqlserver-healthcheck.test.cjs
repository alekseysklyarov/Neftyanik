const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../deploy/docker-compose.production.yml'), 'utf8');
const service = source.match(/^  sqlserver:\r?\n([\s\S]*?)(?=^  \w+:|^volumes:)/m)?.[1];
assert.ok(service, 'The sqlserver service must exist');
const array = service.match(/test:\s*(\[\s*"CMD-SHELL"\s*,\s*"(?:\\.|[^"\\])*"\s*\])/);
assert.ok(array, 'The healthcheck must contain a CMD-SHELL flow sequence');
const [, command] = JSON.parse(array[1]);
const shellCommand = command.replace(/\$\$/g, '$');
const bash = process.env.BASH_PATH || (process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'bash');

function execute(tool, sqlcmdExit) {
    const password = randomBytes(24).toString('hex') + ' ! $ " \' spaces';
    const env = { ...process.env, BASH_ENV: '', ENV: '', MSSQL_SA_PASSWORD: password,
        MOCK_TOOL: tool, MOCK_EXIT: String(sqlcmdExit) };
    delete env.SQLCMDPASSWORD;
    const harness = `
set -euo pipefail
called=0
capture() {
    local tool="$1"
    shift
    [[ "$tool" == "$MOCK_TOOL" ]] || return 91
    [[ "\${SQLCMDPASSWORD-}" == "$MSSQL_SA_PASSWORD" ]] || return 92
    for argument in "$@"; do
        [[ "$argument" != -P && "$argument" != *"$MSSQL_SA_PASSWORD"* ]] || return 93
    done
    [[ "$#" == 8 && "$1" == -S && "$2" == 127.0.0.1 && "$3" == -U && "$4" == sa
        && "$5" == -C && "$6" == -Q && "$7" == 'SELECT 1' && "\${8}" == -b ]] || return 94
    called=$((called + 1))
    printf 'mock output must be suppressed\\n'
    if [[ "$MOCK_EXIT" != 0 ]]; then
        printf 'mock sqlcmd failure\\n' >&2
    fi
    return "$MOCK_EXIT"
}
function /opt/mssql-tools18/bin/sqlcmd { capture tools18 "$@"; }
function /opt/mssql-tools/bin/sqlcmd { capture legacy "$@"; }
function [ {
    if [[ "$#" == 3 && "$1" == -x && "$2" == /opt/mssql-tools18/bin/sqlcmd && "$3" == ']' ]]; then
        [[ "$MOCK_TOOL" == tools18 ]]
    else
        builtin [ "$@"
    fi
}
${shellCommand}
[[ "$called" == 1 ]] || exit 95
[[ ! -v SQLCMDPASSWORD ]] || exit 96
`;
    const result = spawnSync(bash, ['--noprofile', '--norc', '-s'], {
        input: harness, encoding: 'utf8', env, timeout: 10000
    });
    assert.ifError(result.error);
    assert.ok(!result.stdout.includes(password) && !result.stderr.includes(password), 'Password must not appear in output');
    assert.equal(result.status, sqlcmdExit === 0 ? 0 : 1);
    assert.equal(result.stdout, '');
    assert.equal(result.stderr, sqlcmdExit === 0 ? '' : 'mock sqlcmd failure\n');
}

for (const tool of ['tools18', 'legacy']) {
    test(`${tool}: SELECT 1 uses an environment password, not command arguments`, () => {
        execute(tool, 0);
    });
    test(`${tool}: sqlcmd failure makes the healthcheck fail without password disclosure`, () => {
        execute(tool, 17);
    });
}

test('healthcheck preserves Compose dollar escaping and contains no password argument', () => {
    assert.doesNotMatch(command, /(?<!\$)\$(?!\$)/);
    assert.ok(command.includes('SQLCMDPASSWORD="$$MSSQL_SA_PASSWORD" "$$SQLCMD"'));
    assert.doesNotMatch(command, /(?:^|\s)-P(?:\s|$)/);
});

test('expanded healthcheck passes shell syntax validation without execution', () => {
    const result = spawnSync(bash, ['--noprofile', '--norc', '-n'], {
        input: shellCommand, encoding: 'utf8', timeout: 10000, env: { ...process.env, BASH_ENV: '', ENV: '' }
    });
    assert.ifError(result.error);
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
});
