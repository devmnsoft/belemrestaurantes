"""Collect public PA food-business records for explicit, local moderation imports.

Only establishment fields are retained. Resumes opaque server cursors and obeys
429 Retry-After; never changes IPs/endpoints to bypass server limits.
"""
import argparse
import datetime
import json
import pathlib
import re
import shutil
import subprocess
import time
import urllib.parse

parser = argparse.ArgumentParser()
parser.add_argument('--output', default='artifacts/food-registry')
parser.add_argument('--target', type=int, default=6800)
parser.add_argument('--delay', type=int, default=15)
args = parser.parse_args()
root = pathlib.Path(args.output)
root.mkdir(parents=True, exist_ok=True)
fields = ['cnpj', 'nome_fantasia', 'situacao_cadastral', 'cnae_fiscal',
          'cnae_fiscal_descricao', 'uf', 'municipio', 'codigo_municipio_ibge',
          'logradouro', 'descricao_tipo_de_logradouro', 'numero', 'bairro', 'ddd_telefone_1']
base = 'https://minhareceita.org/?uf=PA&cnae_fiscal=5611201,5611203,1091102,4721102&limit=1024'
data_file = root / 'food-pages.ndjson'
checkpoint = root / 'checkpoint.json'
manifest_file = root / 'manifest.json'
manifest = json.loads(manifest_file.read_text(encoding='utf-8')) if manifest_file.exists() else []
qualifying = set()
seen = set()


def count(items):
    for item in items:
        seen.add(item.get('cnpj'))
        if item.get('uf') == 'PA' and item.get('situacao_cadastral') == 2 and str(item.get('nome_fantasia') or '').strip():
            qualifying.add(item['cnpj'])


if data_file.exists():
    for line in data_file.read_text(encoding='utf-8-sig').splitlines():
        if line.strip():
            count(json.loads(line)['data'])
cursor = None
recover_last_page = False
if checkpoint.exists():
    state = json.loads(checkpoint.read_text(encoding='utf-8'))
    if state.get('complete'):
        print('Dataset already complete; reuse the existing files.', flush=True)
        raise SystemExit(0)
    cursor = state.get('cursor')
elif manifest:
    # Recovery from an older collector which did not persist the response cursor.
    cursor = urllib.parse.parse_qs(urllib.parse.urlparse(manifest[-1]['url']).query).get('cursor', [None])[0]
    recover_last_page = True

curl = shutil.which('curl.exe') or shutil.which('curl')
if not curl:
    raise SystemExit('curl is required.')
with data_file.open('a', encoding='utf-8') as output:
    while len(manifest) < 120 and len(qualifying) < min(max(args.target, 1), 20000):
        url = base + ('&cursor=' + urllib.parse.quote(str(cursor), safe='') if cursor else '')
        payload = None
        for attempt in range(5):
            headers_file = root / 'response-headers.txt'
            response = subprocess.run([curl, '--silent', '--show-error', '--fail', '--max-time', '60',
                                       '--dump-header', str(headers_file), url], capture_output=True)
            if response.returncode == 0:
                payload = json.loads(response.stdout)
                break
            headers = headers_file.read_text(encoding='utf-8', errors='replace') if headers_file.exists() else ''
            retry = re.search(r'^retry-after:\s*(\d+)', headers, re.I | re.M)
            is_limited = ' 429 ' in headers
            if not is_limited and not any(f' {code} ' in headers for code in (502, 503, 504)) and response.returncode != 28:
                print('Stopped by HTTP/network failure:', response.stderr.decode(errors='replace')[:180], flush=True)
                break
            seconds = max(60, int(retry.group(1)) if retry else 60)
            print(f'Server backoff: {seconds}s, attempt {attempt + 1}/5; cursor preserved.', flush=True)
            time.sleep(seconds)
        if payload is None or not isinstance(payload.get('data'), list):
            print('Collection paused; rerun to resume the same cursor.', flush=True)
            break
        safe = [{key: item.get(key) for key in fields} for item in payload['data']]
        count(safe)
        stamp = datetime.datetime.now(datetime.timezone.utc).isoformat()
        if not recover_last_page:
            output.write(json.dumps({'fetchedAt': stamp, 'sourceUrl': url, 'data': safe}, ensure_ascii=False, separators=(',', ':')) + '\n')
            output.flush()
            manifest.append({'page': len(manifest), 'fetchedAt': stamp, 'returned': len(safe),
                             'activeNamedAccumulated': len(qualifying), 'url': url})
            manifest_file.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
        recover_last_page = False
        next_cursor = payload.get('cursor')
        if next_cursor is not None and next_cursor == cursor:
            print('Repeated cursor: collection paused without advancing.', flush=True)
            break
        checkpoint.write_text(json.dumps({'cursor': next_cursor, 'complete': not bool(next_cursor)}), encoding='utf-8')
        print(f'pages={len(manifest)} seen={len(seen)} active_named={len(qualifying)}', flush=True)
        if not next_cursor:
            break
        cursor = next_cursor
        time.sleep(max(12, args.delay))
print(f'Local collection: {len(qualifying)} active named candidates; no records published.', flush=True)
