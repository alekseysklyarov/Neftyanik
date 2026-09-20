const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../deploy/scripts/create-sql-backup.sh'), 'utf8');
const bash = process.env.BASH_PATH || (process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : 'bash');
const runSql = source.match(/^run_sql\(\) \{\r?\n[\s\S]*?^\}/m)?.[0];

function execute(tool, sql, exitCode = 0) {
    assert.ok(runSql, 'The shared run_sql function must exist');
    const password = randomBytes(24).toString('hex') + ' ! $ " \' spaces';
    const env = { ...process.env, BASH_ENV: '', ENV: '', MSSQL_SA_PASSWORD: password,
        MOCK_TOOL: tool, EXPECTED_SQL: sql, MOCK_EXIT: String(exitCode) };
    delete env.SQLCMDPASSWORD;
    const harness = `
set -euo pipefail
compose=(docker compose --env-file unused.env -f unused.yml)
SQLSERVER_SERVICE_NAME=sqlserver

capture() {
    local tool="$1"
    shift
    [[ "$tool" == "$MOCK_TOOL" ]] || return 91
    [[ "\${SQLCMDPASSWORD-}" == "$MSSQL_SA_PASSWORD" ]] || return 92
    for argument in "$@"; do
        [[ "$argument" != -P && "$argument" != *"$MSSQL_SA_PASSWORD"* ]] || return 93
    done
    [[ "$#" == 8 && "$1" == -S && "$2" == 127.0.0.1 && "$3" == -U && "$4" == sa
        && "$5" == -C && "$6" == -b && "$7" == -Q && "\${8}" == "$EXPECTED_SQL" ]] || return 94
    printf 'mock sqlcmd: %s\\n' "$tool"
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

docker() (
    for argument in "$@"; do
        [[ "$argument" != *"$MSSQL_SA_PASSWORD"* ]] || return 95
    done
    while [[ "$#" -gt 0 && "$1" != bash ]]; do shift; done
    [[ "$#" == 5 && "$2" == -lc && "$4" == bash ]] || return 96
    local command="$3"
    set -- "$5"
    eval "$command"
)

${runSql}
run_sql "$EXPECTED_SQL"
[[ ! -v SQLCMDPASSWORD ]] || exit 97
`;
    const result = spawnSync(bash, ['--noprofile', '--norc', '-s'], {
        input: harness, encoding: 'utf8', env, timeout: 10000
    });
    assert.ifError(result.error);
    assert.ok(!result.stdout.includes(password) && !result.stderr.includes(password), 'Password must not appear in output');
    assert.equal(result.status, exitCode);
    assert.equal(result.stdout, `mock sqlcmd: ${tool}\n`);
    assert.equal(result.stderr, '');
}

for (const tool of ['tools18', 'legacy']) {
    for (const sql of [
        "BACKUP DATABASE [test-only] TO DISK = N'/mock/test.bak' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;",
        "RESTORE VERIFYONLY FROM DISK = N'/mock/test.bak' WITH CHECKSUM;"
    ]) {
        test(`${tool}: ${sql.split(' ')[0]} uses environment password without argument or output disclosure`, () => {
            execute(tool, sql);
        });
    }
    test(`${tool}: sqlcmd failure propagates without password disclosure`, () => {
        execute(tool, 'mock SQL failure', 17);
    });
}

test('backup and verification statements still use the shared SQL helper unchanged', () => {
    assert.ok(source.includes('backup_sql="BACKUP DATABASE [${database_identifier}] TO DISK = N\'${container_backup_path}\' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;"'));
    assert.ok(source.includes('verify_sql="RESTORE VERIFYONLY FROM DISK = N\'${container_backup_path}\' WITH CHECKSUM;"'));
    assert.equal((source.match(/^run_sql "\$(?:backup|verify)_sql"$/gm) || []).length, 2);
});

test('backup script passes bash syntax validation without execution', () => {
    const result = spawnSync(bash, ['--noprofile', '--norc', '-n'], {
        input: source, encoding: 'utf8', timeout: 10000, env: { ...process.env, BASH_ENV: '', ENV: '' }
    });
    assert.ifError(result.error);
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
});
