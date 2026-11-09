"""Local transport fixture; never contacts model providers or the network."""
import json, pathlib, sys, uuid

kind, logfile = sys.argv[1:3]
args = sys.argv[3:]


def log(event, **values):
    with open(logfile, 'a', encoding='utf-8') as stream:
        stream.write(json.dumps(dict(provider=kind, event=event, **values)) + '\n')


def send(value):
    print(json.dumps(value), flush=True)


settings = {}
if '--settings' in args:
    settings = json.loads(pathlib.Path(args[args.index('--settings') + 1]).read_text(encoding='utf-8-sig'))
log('start', args=args, settings=settings)
if kind == 'codex' and 'exec' in args:
    text = sys.stdin.read()
    pathlib.Path(args[args.index('-o') + 1]).write_text('Fixture cold report', encoding='utf-8')
    log('cold', text=text)
    sys.exit(0)
if kind == 'claude' and '--input-format' not in args:
    log('cold', text=sys.stdin.read())
    send(dict(type='result', is_error=False, stop_reason='end_turn', result='Fixture cold report'))
    sys.exit(0)
for line in sys.stdin:
    try:
        request = json.loads(line)
    except ValueError:
        continue
    if kind == 'claude':
        log('turn', message=request)
        send(dict(type='result', is_error=False, stop_reason='end_turn', result='Fixture report'))
        break
    if 'id' not in request:
        continue
    method = request.get('method')
    result = {}
    if method == 'initialize':
        result = dict(codexHome=str(pathlib.Path(logfile).parent / 'fake-home'))
    elif method == 'windowsSandbox/readiness':
        result = dict(status='ready')
    elif method == 'project/create':
        result = dict(project=dict(id='fixture-project'))
    elif method == 'thread/start':
        result = dict(thread=dict(id=str(uuid.uuid4())))
        log('thread', params=request['params'])
    elif method == 'thread/loaded/list':
        result = dict(data=[])
    elif method == 'turn/start':
        log('turn', params=request['params'])
        send(dict(id=request['id'], result=dict(turn=dict(id='fixture-turn'))))
        send(dict(method='item/completed', params=dict(item=dict(type='agentMessage', text='Fixture report'))))
        send(dict(method='turn/completed', params=dict(turn=dict(status='completed'))))
        continue
    send(dict(id=request['id'], result=result))
