#!/usr/bin/env python3
"""Execute only the exact user-approved cleanup plan, in guarded phases."""
import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
TASK = ROOT / 'tmp/sessions/2026-10-10-lab-structure-audit'
PLAN = TASK / 'cleanup-plan.json'
REPORT = TASK / 'evidence/execution-report.json'

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def path(s):
    p = Path(s)
    assert not p.is_absolute() and '..' not in p.parts, s
    p = ROOT / p
    assert p.resolve().is_relative_to(ROOT), s
    return p

def check(p, expected):
    actual = sha(p) if p.is_file() else None
    assert actual == expected, f'Changed/missing/collision: {p.relative_to(ROOT)}'

def save(r):
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    r['updated_at'] = datetime.now(timezone.utc).isoformat()
    REPORT.write_text(json.dumps(r, ensure_ascii=False, indent=2) + '\n')

def copy_exact(source, target, expected):
    check(source, expected)
    if target.exists():
        check(target, expected)
        return 'already_verified'
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('xb') as out:
        out.write(source.read_bytes())
    check(target, expected)
    return 'installed'

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('phase', choices=['install', 'documents', 'delete', 'preserve', 'verify'])
    args = ap.parse_args()
    plan = json.loads(PLAN.read_text())
    r = json.loads(REPORT.read_text()) if REPORT.exists() else {
        'status': 'EXECUTING', 'authorization': 'User: 无误，可进行',
        'plan_sha256': sha(PLAN), 'installations': [], 'documents': [],
        'source_actions': [], 'empty_directories': [], 'audit_preservation': []}
    assert r['plan_sha256'] == sha(PLAN), 'Approved plan changed'
    if args.phase == 'install':
        subprocess.run([sys.executable, str(TASK/'staging/preflight_cleanup.py')], check=True)
        for item in plan['archive_installations']:
            status = copy_exact(path(item['prepared']), path(item['destination']), item['prepared_sha256'])
            if not any(v['path'] == item['destination'] for v in r['installations']):
                r['installations'].append({'path': item['destination'], 'sha256': item['prepared_sha256'], 'status': status})
            save(r)
        r['installation_complete'] = True
    elif args.phase == 'documents':
        assert r.get('installation_complete')
        for edit in plan['document_edits']:
            p = path(edit['path'])
            if any(v['path'] == edit['path'] for v in r['documents']):
                continue
            if edit.get('depends_on_archive_target'):
                assert path(edit['depends_on_archive_target']).is_file()
            check(p, edit['current_sha256'])
            if edit.get('prepared'):
                src = path(edit['prepared'])
                check(src, edit['prepared_sha256'])
                content = src.read_text()
            else:
                assert p.read_text().count(edit['old_text']) == 1
                content = p.read_text().replace(edit['old_text'], edit['new_text'])
            if edit.get('requires_unity_gate'):
                src = TASK/'staging/approved-AO.md'
                src.write_text(content)
                result = subprocess.run([sys.executable, '.mcp/validation/gate_cli.py', 'write', '--path', edit['path'], '--from', str(src)], cwd=ROOT, capture_output=True, text=True)
                assert result.returncode == 0, result.stdout + result.stderr
                r['ao_gate_result'] = result.stdout
            else:
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(content)
            assert p.read_text() == content
            r['documents'].append({'path': edit['path'], 'sha256': sha(p), 'status': 'applied'})
            save(r)
    elif args.phase == 'delete':
        assert r.get('installation_complete') and len(r['documents']) == 4
        proof = json.loads((TASK/'evidence/installed-validation.json').read_text())
        assert proof['status'] == 'PASS', 'Installed validators must pass first'
        for item in proof['checks']:
            check(path(item['path']), item['sha256'])
        for item in plan['archive_installations']:
            check(path(item['destination']), item['prepared_sha256'])
        pending = [v for v in plan['source_actions'] if not any(a['path'] == v['path'] for a in r['source_actions'])]
        for item in pending:
            check(path(item['path']), item['sha256'])
            if item.get('target_sha256'):
                check(path(item['target']), item['target_sha256'])
        mine = [v['path'] for v in pending if v['path'].startswith('Assets/Mine/')]
        if mine:
            result = subprocess.run([sys.executable, '.mcp/validation/gate_cli.py', 'delete', '--reason', '执行用户已批准的 Lab 清理清单；SHA256 核对通过，归档及正式入口复跑通过。', *mine], cwd=ROOT, capture_output=True, text=True)
            assert result.returncode == 0, result.stdout + result.stderr
            r['mine_delete_gate_result'] = result.stdout
        for item in pending:
            p = path(item['path'])
            if not item['path'].startswith('Assets/Mine/'):
                check(p, item['sha256'])
                p.unlink()
            assert not p.exists()
            r['source_actions'].append({'path': item['path'], 'action': item['action'], 'bytes': item['size_bytes'], 'original_sha256': item['sha256'], 'status': 'removed'})
            save(r)
        for directory in plan['prune_empty_only']:
            p = path(directory)
            status = 'already_absent'
            if p.exists():
                if directory.startswith('Assets/Mine/'):
                    r['empty_directories'].append({'path': directory, 'status': 'retained_nonempty' if any(p.iterdir()) else 'retained_empty_gate_supports_files_only'})
                    continue
                try:
                    p.rmdir()
                    status = 'removed_empty'
                except OSError:
                    status = 'retained_nonempty'
            r['empty_directories'].append({'path': directory, 'status': status})
    elif args.phase == 'preserve':
        for item in plan['audit_preservation']:
            src, dest = path(item['source']), path(item['destination'])
            expected = sha(src)
            status = copy_exact(src, dest, expected)
            r['audit_preservation'].append({'path': item['destination'], 'sha256': expected, 'status': status})
    elif args.phase == 'verify':
        errors = []
        for item in plan['archive_installations']:
            try:
                check(path(item['destination']), item['prepared_sha256'])
            except AssertionError as e:
                errors.append(str(e))
        for item in plan['source_actions']:
            if path(item['path']).exists():
                errors.append('Approved old source remains: '+item['path'])
        inv = json.loads(path(plan['inventory']['path']).read_text())
        kept = [v for v in inv['records'] if v['exists'] and v['action'].startswith('keep')]
        runtime_updates = []
        for item in kept:
            if item['path'] in {'.mcp/state.json', '.mcp/friction.jsonl', '.mcp/deletes.jsonl', '.mcp/moves.jsonl'}:
                p = path(item['path'])
                assert p.is_file(), item['path']
                runtime_updates.append({'path': item['path'], 'inventory_sha256': item['sha256'], 'current_sha256': sha(p), 'reason': 'Production gate state/audit retained; expected updates from authorized gate operations.'})
                continue
            try:
                check(path(item['path']), item['sha256'])
            except AssertionError as e:
                errors.append(str(e))
        r['verification'] = {'status': 'FAIL' if errors else 'PASS', 'installations': 113, 'old_sources': 319, 'retained_files_checked': len(kept), 'runtime_state_preserved': runtime_updates, 'errors': errors}
        r['status'] = 'FAIL' if errors else 'COMPLETE'
        assert not errors, errors
    save(r)
    print(json.dumps({'phase': args.phase, 'status': r['status'], 'installed': len(r['installations']), 'documents': len(r['documents']), 'removed_sources': len(r['source_actions'])}, ensure_ascii=False))

if __name__ == '__main__':
    main()
