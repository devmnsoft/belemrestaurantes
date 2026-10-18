"""Offline checks for collection boundaries, privacy projection and resumability."""
import contextlib
import io
import json
import pathlib
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = pathlib.Path(__file__).with_name('fetch-food-registry.py')


class CollectorTests(unittest.TestCase):
    def run_collector(self, root, responses, *extra):
        calls = []

        def request(command, **kwargs):
            calls.append(command[-1])
            response=responses.pop(0)
            if '_http_error' in response:
                (root/'response-headers.txt').write_text(f"HTTP/2 {response['_http_error']}\nRetry-After: 60\n")
                return subprocess.CompletedProcess(command,22,b'',b'rate limited')
            return subprocess.CompletedProcess(command, 0, json.dumps(response).encode(), b'')

        with patch.object(sys, 'argv', [str(SCRIPT), '--output', str(root), '--target', '100000', *extra]), \
             patch('subprocess.run', side_effect=request), patch('shutil.which', return_value='curl'), \
             patch('time.sleep'), contextlib.redirect_stdout(io.StringIO()):
            try:
                runpy.run_path(str(SCRIPT), run_name='__main__')
            except SystemExit as result:
                if result.code not in (None, 0):
                    raise
        return calls

    def test_large_target_uses_server_cursor_and_minimizes_fields(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            calls = self.run_collector(root, [
                {'data': [{'cnpj': '11222333000181', 'nome_fantasia': 'Teste', 'uf': 'PA',
                           'situacao_cadastral': 2, 'qsa': ['private'], 'email': 'private'}], 'cursor': 'opaque/one'},
                {'data': [], 'cursor': None}])
            self.assertIn('cursor=opaque%2Fone', calls[1])
            data = json.loads((root / 'food-pages.ndjson').read_text().splitlines()[0])['data'][0]
            self.assertNotIn('qsa', data)
            self.assertNotIn('email', data)
            self.assertTrue(json.loads((root / 'checkpoint.json').read_text())['complete'])
            self.assertEqual([], self.run_collector(root, []))

    def test_profile_change_cannot_reuse_old_cursor(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            self.run_collector(root, [{'data': [], 'cursor': None}])
            with self.assertRaisesRegex(SystemExit, 'different query'):
                self.run_collector(root, [], '--profile', 'expanded')

    def test_checkpoint_without_query_or_manifest_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            (root / 'checkpoint.json').write_text('{"cursor":"unknown","complete":false}')
            with self.assertRaisesRegex(SystemExit, 'associate this cursor'):
                self.run_collector(root, [])

    def test_repeated_cursor_keeps_last_safe_checkpoint(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            with self.assertRaises(SystemExit) as result:
                self.run_collector(root, [{'data': [], 'cursor': 'same'}, {'data': [], 'cursor': 'same'}])
            self.assertEqual(2, result.exception.code)
            state = json.loads((root / 'checkpoint.json').read_text())
            self.assertEqual('same', state['cursor'])
            self.assertFalse(state['complete'])

    def test_rate_limit_pauses_after_five_attempts_without_advancing_cursor(self):
        with tempfile.TemporaryDirectory() as folder:
            root=pathlib.Path(folder)
            responses=[{'_http_error':429} for _ in range(5)]
            with self.assertRaises(SystemExit) as result:
                self.run_collector(root,responses)
            self.assertEqual(2,result.exception.code)
            self.assertEqual([],responses)
            self.assertFalse((root/'checkpoint.json').exists())
            self.assertEqual('',(root/'food-pages.ndjson').read_text())


if __name__ == '__main__':
    unittest.main()
