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
        control = root/'SAEF'/'engineering'/'control'
        artifact = root/'SAEF'/'engineering'/'work-products'/'G1'/'G1-03'
        control.mkdir(parents=True, exist_ok=True)
        artifact.mkdir(parents=True, exist_ok=True)
        (artifact/'index.md').write_text('# G1-03 | System requirements\\n\\nReal architecture and requirement prose.\\n', encoding='utf-8')
        (control/'work_products.json').write_text(json.dumps({'G1-03':{
            'path':'engineering/work-products/G1/G1-03/index.md',
            'status':'ACCEPTED','revision':'R1'}}),encoding='utf-8')
        (control/'project_state.json').write_text(json.dumps({'working_phase':'G4','gates':{
            'G1':{'status':'PASS_WITH_LIMITATIONS'},'G4':{'status':'NOT_REVIEWED'}}}),encoding='utf-8')
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

    def test_registered_work_products_are_readable(self):
        _,_,body=self.get('/api/catalog')
        data=json.loads(body)
        self.assertEqual(data['total'],1)
        self.assertEqual(data['status_counts']['ACCEPTED'],1)
        self.assertEqual(next(g for g in data['groups'] if g['id']=='G1')['gate'],'PASS_WITH_LIMITATIONS')
        _,_,body=self.get('/api/document?id=G1-03')
        doc=json.loads(body)
        self.assertIn('Real architecture',doc['body'])
        self.assertEqual(doc['status'],'ACCEPTED')

    def test_evidence_excerpts_and_safe_unknown_document(self):
        _,_,body=self.get('/api/evidence')
        data=json.loads(body)
        self.assertEqual(len(data['nodes']),2)
        self.assertEqual(next(n for n in data['nodes'] if n['id']=='TRACE-01')['line'],1)
        with self.assertRaises(HTTPError) as err:
            self.get('/api/document?id=../../invalid')
        self.assertEqual(err.exception.code,400)

    def test_post_refused(self):
        with self.assertRaises(HTTPError) as err:
            urlopen(Request(self.base+'/api/overview',data=b'{}',method='POST'),timeout=3)
        self.assertEqual(err.exception.code,405)


if __name__=='__main__': unittest.main()
