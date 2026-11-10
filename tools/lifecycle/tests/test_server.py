"""Read-only localhost server endpoint smoke test against generated files."""
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import urlopen, Request
from http.server import ThreadingHTTPServer

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from server import make_handler, UI
from lifecycle import Workspace


class ServerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        (root/'doc.md').write_text('TRACE-01 source',encoding='utf-8')
        manifest = {'schema':'autotest-lifecycle/0.1','nodes':[
            {'id':'TRACE-01','kind':'requirement','path':'doc.md','anchor':'TRACE-01'},
            {'id':'WP','kind':'work_package','status':'DRAFT','required_inputs':['TRACE-01'],'baseline':{},'verification':['unit test']}],
            'edges':[{'from':'TRACE-01','to':'WP','kind':'depends_on','certainty':'review_required'}], 'changes':[]}
        workspace = Workspace(root,manifest)
        self.server = ThreadingHTTPServer(('127.0.0.1',0),make_handler(workspace))
        self.worker = threading.Thread(target=self.server.serve_forever,daemon=True)
        self.worker.start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.base = 'http://127.0.0.1:'+str(self.server.server_address[1])

    def get(self,path):
        with urlopen(self.base+path,timeout=3) as reply:
            return reply.status,reply.headers,reply.read()

    def test_ui_is_local_and_present(self):
        code,headers,body=self.get('/')
        self.assertEqual(code,200)
        self.assertIn('ENGINEERING CONTROL',body.decode('utf-8'))
        self.assertIn('no-store',headers['Cache-Control'])
        self.assertTrue(UI.is_file())

    def test_overview_and_impact(self):
        _,_,body=self.get('/api/overview')
        data=json.loads(body)
        self.assertEqual(len(data['nodes']),2)
        _,_,body=self.get('/api/impact?node=TRACE-01')
        self.assertEqual(json.loads(body)['affected'][0]['id'],'WP')

    def test_readiness_not_ready(self):
        _,_,body=self.get('/api/readiness?package=WP')
        self.assertEqual(json.loads(body)['status'],'NOT_READY')

    def test_post_refused(self):
        with self.assertRaises(HTTPError) as err:
            urlopen(Request(self.base+'/api/overview',data=b'{}',method='POST'),timeout=3)
        self.assertEqual(err.exception.code,405)


if __name__=='__main__': unittest.main()
