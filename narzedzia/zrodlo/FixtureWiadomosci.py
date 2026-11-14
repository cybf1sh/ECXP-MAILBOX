import sys, json, uuid

sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')
kind = sys.argv[1]


def send(value):
    print(json.dumps(value), flush=True)


def answer(text):
    with open(sys.argv[2], 'a', encoding='utf-8') as log:
        log.write(json.dumps({'provider': kind, 'text': text}) + '\n')
    return 'ODEBRANO: ' + text.split('\n\n', 1)[0]


for line in sys.stdin:
    try:
        request = json.loads(line)
    except ValueError:
        continue
    if kind == 'claude':
        send({'type': 'result', 'is_error': False, 'stop_reason': 'end_turn',
              'result': answer(request['message']['content'])})
        break
    if 'id' not in request:
        continue
    method = request.get('method')
    result = {}
    if method == 'initialize':
        result = {'codexHome': sys.argv[2] + '.home'}
    if method == 'project/create':
        result = {'project': {'id': 'fixture-project'}}
    if method == 'thread/start':
        result = {'thread': {'id': str(uuid.uuid4())}}
    if method == 'thread/loaded/list':
        result = {'data': []}
    if method == 'turn/start':
        text = '\n'.join(item.get('text', '') for item in request['params']['input'])
        send({'id': request['id'], 'result': {'turn': {'id': 'fixture-turn'}}})
        send({'method': 'item/completed', 'params': {
            'item': {'type': 'agentMessage', 'text': answer(text)}}})
        send({'method': 'turn/completed', 'params': {'turn': {'status': 'completed'}}})
        continue
    send({'id': request['id'], 'result': result})
