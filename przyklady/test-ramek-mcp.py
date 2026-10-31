"""Test strony agentów: ecxp-mcp (adapter MCP) przez stos ecxpd oraz surowe ramki ECXP na potoku."""
import hashlib, json, os, shutil, struct, subprocess, sys, time
sys.stdout.reconfigure(encoding='utf-8')
B = sys.argv[1]
S = os.path.join(B, 'narzedzia')
root = os.path.join(B, 't3')
shutil.rmtree(root, ignore_errors=True); os.makedirs(root)
wyniki = []
def ok(c, t): wyniki.append(('PASS  ' if c else 'FAIL  ') + t)

proc = subprocess.Popen([os.path.join(S, 'ecxp-mcp.exe'), '--root', root], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
nr = [0]
def rpc(method, params=None):
    nr[0] += 1
    proc.stdin.write((json.dumps({"jsonrpc": "2.0", "id": nr[0], "method": method, "params": params or {}}, ensure_ascii=False) + "\n").encode('utf-8'))
    proc.stdin.flush()
    return json.loads(proc.stdout.readline().decode('utf-8'))
def tool(name, **a):
    r = rpc("tools/call", {"name": name, "arguments": a})['result']
    t = r['content'][0]['text']
    return (json.loads(t) if not r['isError'] else t), r['isError']

init = rpc("initialize", {"protocolVersion": "2025-06-18"})
ok('ZASADA 1:1' in init['result']['instructions'], 'MCP: instrukcje z zasadą 1:1')
names = [t['name'] for t in rpc("tools/list")['result']['tools']]
ok(all(n in names for n in ['ecxp_przypisz', 'ecxp_trasy', 'ecxp_przekaz', 'ecxp_kontekst_zapisz', 'ecxp_zadanie']), 'MCP: %d narzędzi, w tym przypisz/trasy/przekaz/kontekst/zadanie' % len(names))

t0 = time.time()
r, e = tool('ecxp_przypisz', ja='codex-elan', projekt='testowy', modul='elan', sesja='wątek-codex-1', role=['programista'])
ok(not e, 'Codex zgłasza się do pary (stos uruchomiony przez adapter, %.0f ms)' % ((time.time() - t0) * 1000))
r, e = tool('ecxp_przypisz', ja='claude-elan', projekt='testowy', modul='elan', sesja='local_test', role=['recenzent'])
ok(not e and r['para']['pelna'], 'Claude zgłasza się – para pełna')

t0 = time.time()
r, e = tool('ecxp_wyslij', ja='codex-elan', do='claude/elan', projekt='testowy', modul='elan', typ='pytanie', temat='LIMIT', tresc='Limit 8 kB na część?')
dt = (time.time() - t0) * 1000
ok(not e and r['adresaci'] == ['claude-elan'], 'Codex → claude/elan: adres rozwiązany przez tablicę tras (%.1f ms)' % dt)
pyt = r['id']
r, e = tool('ecxp_odbierz', ja='claude-elan')
ok(not e and r['liczba'] == 1 and r['wiadomosci'][0]['integralnosc'] == 'ok', 'Claude odbiera, integralność ok')
r, e = tool('ecxp_wyslij', ja='claude-elan', do='wlasciciel', projekt='testowy', modul='elan', typ='blokada', tresc='Potrzebna decyzja operatora.', odpowiedz_na=pyt)
ok(not e and len(r['kopie_dla_pary']) == 1 and r['kopie_dla_pary'][0]['do'] == 'codex-elan' and r['potwierdzono_przy_okazji'] == pyt,
   'Claude → operator: kopia dla Codexa (1:1) i ACK doklejony do pytania')
r, e = tool('ecxp_stan', ja='codex-elan')
ok(not e and r['czeka_na_mnie'] >= 1 and r['znacznik'] == '🔗ECXP', 'Codex widzi kopię w kolejce, znacznik 🔗ECXP')
r, e = tool('ecxp_kontekst_zapisz', ja='codex-elan', tresc='{"plan":"x"}' * 100, nazwa='plan.json')
sha = r['sha256'] if not e else ''
r2, e2 = tool('ecxp_kontekst_pobierz', ja='claude-elan', sha256=sha, od=0, ile=50)
ok(not e and not e2 and r2['ile'] == 50 and not r2['koniec'], 'kontekst SHA-256 zapisany i pobrany porcją (50 znaków z %d)' % r2.get('calosc', 0))
r, e = tool('ecxp_wyslij', ja='wlasciciel', do='para:elan', projekt='testowy', typ='zadanie', tresc='podszycie')
ok(e and 'zastrzeżone' in r, 'adapter MCP nie może podszyć się pod operatora')
r, e = tool('ecxp_trasy', projekt='testowy')
ok(not e and r['pary'][0]['pelna'], 'ecxp_trasy: para widoczna')
proc.stdin.close(); proc.wait(10)

# --- surowe ramki ECXP na potoku ---
pipe = r'\\.\pipe\ecxp-' + hashlib.sha256(os.path.abspath(root).rstrip('\\').lower().encode('utf-8')).hexdigest()[:12]
def ramka(typ, flagi, seq, ack, ladunek=b'', zepsuj=False):
    h = b'ECXP' + bytes([1, typ]) + struct.pack('<HQQQII', flagi, 0, seq, ack, 16, len(ladunek))
    sk = hashlib.sha256(h + ladunek).digest()
    if zepsuj: sk = bytes([sk[0] ^ 1]) + sk[1:]
    return h + sk + ladunek
def czytaj(f):
    h = f.read(72); n = struct.unpack('<I', h[36:40])[0]
    return h[5], struct.unpack('<H', h[6:8])[0], struct.unpack('<Q', h[16:24])[0], struct.unpack('<Q', h[24:32])[0], f.read(n)

with open(pipe, 'r+b', buffering=0) as f:
    f.write(ramka(1, 1, 1000, 0, json.dumps({"ja": "wlasciciel", "rodzaj": "konsola"}).encode()))
    typ, fl, s, a, ld = czytaj(f)
    ok(typ == 11, 'surowa ramka: obcy proces jako „konsola” dostaje RST (tożsamość operatora po obrazie procesu)')
with open(pipe, 'r+b', buffering=0) as f:
    f.write(ramka(1, 1, 5000, 0, json.dumps({"ja": "*", "rodzaj": "mcp"}).encode()))
    typ, fl, s, a, ld = czytaj(f)
    ok(typ == 2 and fl & 3 == 3 and a == 5001, 'uzgodnienie: HELLO(SYN, seq=5000) → WITAJ(SYN|ACK, ack=5001)')
    f.write(ramka(6, 2, 5001, s + 1))
    f.write(ramka(3, 16, 5002, s + 1, json.dumps({"op": "ping", "args": {}}).encode(), zepsuj=True))
    typ, fl, s2, a2, ld = czytaj(f)
    ok(typ == 9, 'ramka z błędnym SHA-256 odrzucona ramką BLAD')

print('\n'.join(wyniki))
print('WYNIK: ' + ('wszystkie testy PASS' if all(w.startswith('PASS') for w in wyniki) else 'FAIL %d' % sum(not w.startswith('PASS') for w in wyniki)))
