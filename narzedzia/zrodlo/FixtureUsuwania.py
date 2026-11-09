"""Synthetic provider metadata for project-deletion tests. No network."""
import json, pathlib, shutil, sys

kind, filename = sys.argv[1:3]
args = sys.argv[3:]
state_path = pathlib.Path(filename)


def state():
    return json.loads(state_path.read_text(encoding='utf-8-sig'))


def save(value):
    state_path.write_text(json.dumps(value), encoding='utf-8')


if kind == 'claude':
    if '--help' in args:
        print('Delete all Claude Code state for a project --dry-run --yes')
        sys.exit(0)
    data = state()
    project = args[2] if args[:2] == ['project', 'purge'] else args[1]
    if '--dry-run' in args:
        print('Purge plan for ' + project + ':')
        print('  dir: ' + data['claude_dir'])
        sys.exit(0)
    if '--yes' not in args or project != data['project']:
        sys.exit(9)
    if data.get('fail_claude'):
        print('Synthetic purge failure')
        sys.exit(7)
    shutil.rmtree(data['claude_dir'])
    data['claude_deleted'] = project
    save(data)
    print('Deleted project state')
    sys.exit(0)
for line in sys.stdin:
    try:
        request = json.loads(line)
    except ValueError:
        continue
    if 'id' not in request:
        continue
    data = state()
    method = request.get('method')
    params = request.get('params', {})
    result = {}
    if method == 'initialize':
        result = dict(codexHome=data['codex_home'])
    elif method == 'thread/list':
        rows = data['threads']
        if 'cwd' in params:
            rows = [row for row in rows if row['cwd'] == params['cwd']]
        if 'parentThreadId' in params:
            rows = [row for row in rows if row.get('parent') == params['parentThreadId']]
        rows = [row for row in rows if row.get('archived', False) == params.get('archived', False)]
        result = dict(data=rows, nextCursor=None)
    elif method == 'thread/delete':
        target = params['threadId']
        if any(row.get('parent') == target for row in data['threads']):
            print(json.dumps(dict(id=request['id'], error=dict(code=1, message='Children must be deleted first'))), flush=True)
            continue
        data.setdefault('deleted', []).append(target)
        data['threads'] = [row for row in data['threads'] if row['id'] != target]
        save(data)
    print(json.dumps(dict(id=request['id'], result=result)), flush=True)
