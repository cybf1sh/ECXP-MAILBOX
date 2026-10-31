"""Test ECXP 1.1 przez prawdziwy stos (adapter MCP + konsola-operator w osobnym teście):
łańcuchy ról, wspólna gałąź i rozbieżność, decyzje (agent nie może wydać), lustro czatu Claude (hook) i Codex (zapis rozmowy)."""
import json, os, shutil, subprocess, sys, time
sys.stdout.reconfigure(encoding='utf-8')
B = sys.argv[1]                  # katalog z narzedzia\ (programy)
OPER = sys.argv[2]               # katalog testu, w którym konsola (/selftest-stos-2) przygotowała parę i łańcuch
root = OPER
wyniki = []
def ok(c, t): wyniki.append(('PASS  ' if c else 'FAIL  ') + t)

codex_dir = os.path.join(root, 'codex-sessions')
os.makedirs(codex_dir, exist_ok=True)
env = dict(os.environ, ECXP_CODEX_SESSIONS=codex_dir)
proc = subprocess.Popen([os.path.join(B, 'narzedzia', 'ecxp-mcp.exe'), '--root', root], stdin=subprocess.PIPE, stdout=subprocess.PIPE, env=env)
nr = [0]
def rpc(method, params=None):
    nr[0] += 1
    proc.stdin.write((json.dumps({"jsonrpc": "2.0", "id": nr[0], "method": method, "params": params or {}}, ensure_ascii=False) + "\n").encode('utf-8')); proc.stdin.flush()
    return json.loads(proc.stdout.readline().decode('utf-8'))
def tool(name, **a):
    r = rpc("tools/call", {"name": name, "arguments": a})['result']
    t = r['content'][0]['text']
    return (json.loads(t) if not r['isError'] else t), r['isError']
rpc("initialize", {"protocolVersion": "2025-06-18"})
names = [t['name'] for t in rpc("tools/list")['result']['tools']]
ok(all(n in names for n in ['ecxp_umowa_galezi', 'ecxp_stan_galezi', 'ecxp_sprawdz_decyzje', 'ecxp_lancuchy']), 'MCP: nowe narzędzia (%d)' % len(names))

# --- łańcuch ról: krok programista (codex-lr) → tester (claude-lr) → decyzja ---
r, e = tool('ecxp_lancuchy', projekt='testowy')
lan = [l for l in r['lancuchy'] if l['modul'] == 'lr'][0]
ok(lan['krok'] == 0 and lan['kroki'][:3] == ['programista', 'tester', 'decyzja'], 'łańcuch utworzony przez operatora, krok 1: programista')
r, e = tool('ecxp_odbierz', ja='codex-lr')
zad = [m for m in r['wiadomosci'] if m.get('typ') == 'zadanie'][0]
r, e = tool('ecxp_zadanie', ja='codex-lr', id=zad['id'], stan='zakonczone', uwagi='kod gotowy, commit abc')
r, e = tool('ecxp_lancuchy', projekt='testowy')
lan = [l for l in r['lancuchy'] if l['modul'] == 'lr'][0]
ok(lan['krok'] == 1 and lan['stan'] == 'w_toku', 'programista zakończył → stos przekazał do testera (krok 2)')
r, e = tool('ecxp_odbierz', ja='claude-lr')
zt = [m for m in r['wiadomosci'] if m.get('typ') == 'zadanie' and 'tester' in m.get('temat', '').lower()]
ok(len(zt) == 1, 'tester dostał zadanie łańcucha')
tool('ecxp_zadanie', ja='claude-lr', id=zt[0]['id'], stan='nieudane', uwagi='test X nie przechodzi')
r, e = tool('ecxp_lancuchy', projekt='testowy')
lan = [l for l in r['lancuchy'] if l['modul'] == 'lr'][0]
ok(lan['krok'] == 0, 'tester: nieudane → stos zawrócił do programisty')
r, e = tool('ecxp_odbierz', ja='codex-lr')
zad = [m for m in r['wiadomosci'] if m.get('typ') == 'zadanie' and 'nie przechodzi' in m.get('tresc', '')]
ok(len(zad) == 1, 'programista dostał poprawki z uwagami testera')
tool('ecxp_zadanie', ja='codex-lr', id=zad[0]['id'], stan='zakonczone')
r, e = tool('ecxp_odbierz', ja='claude-lr')
zt = [m for m in r['wiadomosci'] if m.get('typ') == 'zadanie' and 'tester' in m.get('temat', '').lower()][-1]
tool('ecxp_zadanie', ja='claude-lr', id=zt['id'], stan='zakonczone')
r, e = tool('ecxp_lancuchy', projekt='testowy')
lan = [l for l in r['lancuchy'] if l['modul'] == 'lr'][0]
ok(lan['stan'] == 'czeka_na_decyzje', 'tester zakończył → łańcuch czeka na decyzję operatora')

# --- decyzje: agent nie wyda, operator (w teście konsoli) wydał – podpis sprawdzalny ---
r, e = tool('ecxp_wyslij', ja='codex-lr', do='para:lr', projekt='testowy', typ='decyzja', tresc='podszyta decyzja')
ok(e and 'operator' in r, 'agent nie może wysłać decyzji')
dec = json.load(open(os.path.join(root, 'decyzja-testowa.txt'), encoding='utf-8'))
r, e = tool('ecxp_sprawdz_decyzje', id=dec['id'])
ok(not e and r['wazna'] is True and r['tresc']['commit'] == dec['commit'], 'podpis decyzji operatora poprawny (RSA, klucz DPAPI)')
p = os.path.join(root, 'stan', 'decyzje', dec['id'] + '.json')
d = json.load(open(p, encoding='utf-8')); d['kanon'] = d['kanon'].replace(dec['commit'], 'f' * 40); json.dump(d, open(p, 'w', encoding='utf-8'))
r, e = tool('ecxp_sprawdz_decyzje', id=dec['id'])
ok(not e and r['wazna'] is False, 'zmieniona treść decyzji → podpis nieważny')
r, e = tool('ecxp_sprawdz_decyzje', id='D-nie-ma')
ok(not e and r['wazna'] is False, 'nieznana decyzja → nieważna')

# --- wspólna gałąź: umowa, STAN_GALEZI, rozbieżność ---
repo = os.path.join(root, 'repo')
os.makedirs(repo)
g = lambda *a: subprocess.run(['git', '-C', repo] + list(a), capture_output=True, text=True).stdout.strip()
g('init', '-q'); g('config', 'user.email', 't@t'); g('config', 'user.name', 't')
open(os.path.join(repo, 'a.txt'), 'w').write('a'); g('add', '.'); g('commit', '-qm', 'baza'); baza = g('rev-parse', 'HEAD')
g('checkout', '-qb', 'feature/lr')
open(os.path.join(repo, 'b.txt'), 'w').write('b'); g('add', '.'); g('commit', '-qm', 'A'); A = g('rev-parse', 'HEAD')
open(os.path.join(repo, 'c.txt'), 'w').write('c'); g('add', '.'); g('commit', '-qm', 'A2'); A2 = g('rev-parse', 'HEAD')
g('checkout', '-qb', 'obok', baza)
open(os.path.join(repo, 'd.txt'), 'w').write('d'); g('add', '.'); g('commit', '-qm', 'B'); Bc = g('rev-parse', 'HEAD')
r, e = tool('ecxp_umowa_galezi', ja='codex-lr', projekt='testowy', modul='lr', repozytorium=repo, galaz='feature/lr', baza=baza)
ok(not e and r['stan'] == 'proponowana', 'Codex proponuje wspólną gałąź')
r, e = tool('ecxp_umowa_galezi', ja='claude-lr', projekt='testowy', modul='lr', repozytorium=repo, galaz='feature/lr', baza=baza)
ok(not e and r['stan'] == 'uzgodniona', 'Claude potwierdza → umowa uzgodniona')
r, e = tool('ecxp_stan_galezi', ja='codex-lr', projekt='testowy', modul='lr', galaz='feature/lr', head=A)
ok(not e and r['para_stan'] == 'ok', 'STAN_GALEZI Codex: HEAD A – zgodnie')
r, e = tool('ecxp_stan_galezi', ja='claude-lr', projekt='testowy', modul='lr', galaz='feature/lr', head=A2)
ok(not e and r['para_stan'] == 'ok', 'STAN_GALEZI Claude: HEAD A2 (potomek A) – zgodnie')
r, e = tool('ecxp_stan_galezi', ja='claude-lr', projekt='testowy', modul='lr', galaz='feature/lr', head=Bc)
ok(not e and r['para_stan'] == 'rozbieznosc', 'HEAD B rozjechany z A → ROZBIEŻNOŚĆ wykryta (git merge-base)')
r, e = tool('ecxp_wyslij', ja='codex-lr', do='claude/lr', projekt='testowy', typ='zadanie', tresc='kolejna zmiana')
ok(e and 'wstrzymana' in r, 'para wstrzymana: agent nie zleci nowego zadania')
r, e = tool('ecxp_odbierz', ja='codex-lr')
ok(any('ROZBIEZNOSC' in m.get('temat', '') for m in r['wiadomosci']), 'alarm STOP dotarł do strony pary')

# --- lustro czatu Claude (hook → stan\wejscia) i Codex (zapis rozmowy) ---
tool('ecxp_odbierz', ja='codex-elan'); tool('ecxp_odbierz', ja='claude-elan2')
hook = os.path.join(B, 'narzedzia', 'hook-claude.ps1')
tr = os.path.join(root, 'transkrypt.jsonl')
open(tr, 'w', encoding='utf-8').write('{"type":"user","message":"Twoja nazwa w ECXP: claude-elan2"}\n')
we = json.dumps({"session_id": "sesja-claude-1234", "transcript_path": tr, "prompt": "Zmień limit na 16 kB, zażółć gęślą jaźń."}, ensure_ascii=False)
t0 = time.time()
subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', hook], input=we.encode('utf-8'), env=dict(os.environ, ECXP_SKRZYNKA=root), capture_output=True, timeout=30)
ok(time.time() - t0 < 5, 'hook Claude: czas %.2f s' % (time.time() - t0))
we2 = json.dumps({"session_id": "obca-sesja", "transcript_path": "", "prompt": "prywatna rozmowa spoza ECXP"}, ensure_ascii=False)
subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', hook], input=we2.encode('utf-8'), env=dict(os.environ, ECXP_SKRZYNKA=root), capture_output=True, timeout=30)
ses = os.path.join(codex_dir, '2026', '10', '31'); os.makedirs(ses, exist_ok=True)
plik = os.path.join(ses, 'rollout-2026-10-31T12-00-00-test.jsonl')
linie = [
    {"type": "session_meta", "payload": {"session_id": "test"}},
    {"type": "event_msg", "payload": {"type": "task_started", "turn_attribution": {"turn_trigger": "composer"}}},
    {"type": "response_item", "payload": {"type": "message", "role": "user", "content": [{"type": "input_text", "text": "Twoja nazwa w ECXP: codex-elan. Pakiet startowy…"}]}},
    {"type": "event_msg", "payload": {"type": "task_started", "turn_attribution": {"turn_trigger": "automation_heartbeat_scheduled"}}},
    {"type": "response_item", "payload": {"type": "message", "role": "user", "content": [{"type": "input_text", "text": "<heartbeat>automatyzacja</heartbeat>"}]}},
    {"type": "event_msg", "payload": {"type": "task_started", "turn_attribution": {"turn_trigger": "composer"}}},
    {"type": "response_item", "payload": {"type": "message", "role": "user", "content": [{"type": "input_text", "text": "<environment_context>x</environment_context>"}]}},
    {"type": "response_item", "payload": {"type": "message", "role": "user", "content": [{"type": "input_text", "text": "Codex: dodaj test regresji dla limitu."}]}},
]
open(plik, 'w', encoding='utf-8').write('\n'.join(json.dumps(x, ensure_ascii=False) for x in linie) + '\n')
time.sleep(9)
r, e = tool('ecxp_odbierz', ja='codex-elan')
ok(any(m.get('temat') == 'CZAT-CLAUDE' and 'gęślą' in m.get('tresc', '') for m in r['wiadomosci']), 'lustro: wpis operatora z czatu Claude dotarł do Codexa (dosłownie)')
ok(not any('prywatna rozmowa' in m.get('tresc', '') for m in r['wiadomosci']), 'sesja Claude spoza ECXP – nic nie przekazano')
r, e = tool('ecxp_odbierz', ja='claude-elan2')
cz = [m for m in r['wiadomosci'] if m.get('temat') == 'CZAT-CODEX']
ok(len(cz) == 1 and 'test regresji' in cz[0]['tresc'], 'lustro: wpis operatora z czatu Codexa dotarł do Claude (bez pakietu, automatyzacji i kontekstu)')

proc.stdin.close(); proc.wait(10)
print('\n'.join(wyniki))
print('WYNIK: ' + ('wszystkie testy PASS' if all(w.startswith('PASS') for w in wyniki) else 'FAIL %d' % sum(not w.startswith('PASS') for w in wyniki)))
