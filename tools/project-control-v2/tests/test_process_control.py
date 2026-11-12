import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'portable'))
from process_control import (catalog, initial_state, state_for, apply, assessment, ProcessError)
from project_init import inspect_target, create_preview, apply_preview
from web_server import Target

REPO = 'https://github.com/example/pc-process-test.git'

class ProcessControlTests(unittest.TestCase):
    def setUp(self):
        self.record = {'schema': 'project-control-target-management/1', 'project_id': 'demo',
                       'revision': 1, 'deliverables': [], 'work': [], 'milestones': [],
                       'risks': [], 'progress': {}, 'events': [], 'modules': []}

    def open(self):
        apply(self.record, 'open_process_activity', {'id': 'p0-restart', 'process_id': 'initiation',
              'role': 'coordinator', 'objective': '重新识别项目目标而不承认旧 Gate',
              'source_refs': ['用户当前消息']})
        return state_for(self.record)['activities'][0]

    def test_catalog_has_procedures_not_fixed_gate_model(self):
        data = catalog()
        self.assertEqual(data['version'], '1.2.0')
        self.assertIn('initiation', {x['id'] for x in data['processes']})
        self.assertIn('research', {x['id'] for x in data['profiles']})
        self.assertNotIn('G4', [x['id'] for x in data['processes']])

    def test_new_activity_has_all_open_obligations_and_no_permission(self):
        act = self.open()
        self.assertGreaterEqual(len(act['obligations']), 7)
        self.assertEqual(assessment(act)['state'], 'NOT_READY')
        self.assertIsNone(act['technical_review'])
        self.assertEqual(self.record['process_control']['activities'][0]['state'], 'DRAFT_NOT_APPROVED')

    def test_no_duplicate_activity_or_wrong_role(self):
        self.open()
        with self.assertRaises(ProcessError):
            self.open()
        with self.assertRaises(ProcessError):
            apply(self.record, 'open_process_activity', {'id':'design-test', 'process_id': 'detailed-design',
              'role':'coordinator', 'objective': '不允许自封设计', 'source_refs':['source']})

    def test_missing_evidence_and_core_optout_block(self):
        self.open()
        with self.assertRaises(ProcessError):
            apply(self.record, 'set_process_obligation', {'activity_id':'p0-restart','obligation_id':'purpose',
              'disposition':'SATISFIED_CANDIDATE','reason':'已整理','evidence_refs':[]})
        with self.assertRaises(ProcessError):
            apply(self.record, 'set_process_obligation', {'activity_id':'p0-restart','obligation_id':'purpose',
              'disposition':'NOT_APPLICABLE_PROPOSED','reason':'不想做','impact':'无'})

    def test_tailoring_is_proposal_not_approval(self):
        self.open()
        apply(self.record, 'set_process_obligation', {'activity_id':'p0-restart','obligation_id':'external-commercial',
              'disposition':'NOT_APPLICABLE_PROPOSED','reason':'仅内部使用','impact':'以后外部销售需再审'})
        self.assertEqual(assessment(self.record['process_control']['activities'][0])['state'], 'NOT_READY')
        self.assertEqual(self.record['process_control']['activities'][0]['state'], 'DRAFT_NOT_APPROVED')

    def test_conditional_tailoring_can_be_submitted_only_as_pending_owner_decision(self):
        a=self.open()
        for o in a['obligations']:
            d='NOT_APPLICABLE_PROPOSED' if o['id']=='external-commercial' else 'SATISFIED_CANDIDATE'
            q={'activity_id':a['id'],'obligation_id':o['id'],'disposition':d,
               'reason':'需要负责人审查','evidence_refs':['charter.md']}
            if d!='SATISFIED_CANDIDATE':q['impact']='目前没有外部销售，范围变化需要新决定'
            apply(self.record,'set_process_obligation',q)
        apply(self.record,'review_process_activity',{'activity_id':a['id'],
          'reviewer_role':'reviewer','outcome':'ADEQUATE_CANDIDATE','reason':'待人类确认裁剪',
          'evidence_refs':['review.md']})
        self.assertEqual(assessment(a)['state'],'READY_FOR_OWNER_DECISION_WITH_PROPOSALS')
        apply(self.record,'request_process_review',{'id':'owner-1','activity_id':a['id'],'purpose':'裁剪须确认'})
        req=self.record['process_control']['review_requests'][0]
        self.assertEqual(req['state'],'REQUESTED_NOT_APPROVED')
        self.assertTrue(req['pending_decisions'])

    def test_deferral_exception_need_owner_closure(self):
        self.open()
        p={'activity_id':'p0-restart','obligation_id':'constraints',
           'disposition':'DEFERRED_PROPOSED', 'reason':'需实测','impact':'影响硬件范围'}
        with self.assertRaises(ProcessError):apply(self.record,'set_process_obligation',p)
        p.update(owner='工程负责人',close_by='现场风险审查')
        apply(self.record,'set_process_obligation',p)
        self.assertEqual(assessment(self.record['process_control']['activities'][0])['state'],'NOT_READY')

    def test_request_is_structural_only_and_stales_on_change(self):
        a=self.open()
        for o in a['obligations']:
            apply(self.record,'set_process_obligation',{'activity_id':a['id'],'obligation_id':o['id'],
              'disposition':'SATISFIED_CANDIDATE','reason':'文档已成候选', 'evidence_refs':['docs/charter.md']})
        with self.assertRaises(ProcessError):
            apply(self.record,'request_process_review',{'id':'review-1','activity_id':a['id'],'purpose':'审批 P0'})
        apply(self.record,'review_process_activity',{'activity_id':a['id'],
          'reviewer_role':'reviewer','outcome':'ADEQUATE_CANDIDATE','reason':'有来源但不代表实际批准',
          'evidence_refs':['review.md'], 'independent_review': True})
        self.assertEqual(assessment(a)['state'],'READY_FOR_OWNER_REVIEW')
        apply(self.record,'request_process_review',{'id':'review-1','activity_id':a['id'],'purpose':'请负责人审查'})
        req=self.record['process_control']['review_requests'][0]
        self.assertEqual(req['state'],'REQUESTED_NOT_APPROVED')
        apply(self.record,'set_process_obligation',{'activity_id':a['id'],'obligation_id':'purpose',
          'disposition':'CONFLICT','reason':'旧定义被撤回','impact':'必须重审目的'})
        self.assertEqual(assessment(a)['state'],'NOT_READY')
        self.assertEqual(req['state'],'STALE_AFTER_REVISION')

    def test_self_review_cannot_claim_independent(self):
        self.open()
        with self.assertRaises(ProcessError):
            apply(self.record,'review_process_activity',{'activity_id':'p0-restart',
              'reviewer_role':'coordinator','outcome':'ADEQUATE_CANDIDATE','reason':'我觉得可以',
              'evidence_refs':['self'],'independent_review':True})

    def test_ai_intake_is_not_role_grant_and_reports_not_acceptance(self):
        apply(self.record, 'record_ai_intake', {'id':'intake-001','role':'coordinator',
           'role_source':'USER_EXPLICIT','source_refs':['main@sha','用户角色确认'],
           'verified_facts':['有主线资产'], 'conflicts':['历史 P0 已撤销'],
           'unknowns':['实际 P0 范围'], 'next_action':'先重审立项目的'})
        entry=self.record['process_control']['intakes'][0]
        self.assertEqual(entry['state'],'ROLE_REPORTED_NOT_AUTHORIZED')
        self.assertEqual(entry['write_permission'],'NONE_FROM_INTAKE')
        with self.assertRaises(ProcessError):
            apply(self.record,'record_ai_work_report',{'id':'report-1','intake_id':'intake-001',
              'summary':'调查完成','evidence_refs':[], 'next_action':'继续'})
        apply(self.record,'record_ai_work_report',{'id':'report-1','intake_id':'intake-001',
          'focus':'重建立项的业务动因','why_this_work':'需要先确认项目为何存在再进行设计',
          'judgement':'已有资料能支持候选目标，但新范围尚需重新核对',
          'owner_action':'REVIEW','owner_request':'审阅立项目的候选',
          'summary':'调查完成','evidence_refs':['source.md'],'unfinished':['审批'], 'next_action':'提交候审'})
        self.assertEqual(self.record['process_control']['reports'][0]['state'],'REPORTED_NOT_ACCEPTED')

    def test_policy_mismatch_rejects_implicit_upgrade(self):
        self.record['process_control']=initial_state()
        self.record['process_control']['catalog_version']='0.5'
        with self.assertRaises(ProcessError):state_for(self.record)
        self.record['process_control']=initial_state()
        self.record['process_control']['catalog_sha256']='0'*64
        with self.assertRaises(ProcessError):state_for(self.record)

    def test_old_governance_not_overwritten(self):
        self.record['events']=[{'type':'historical','note':'legacy approval is only historical'}]
        before=copy.deepcopy(self.record['events'])
        self.open()
        self.assertEqual(self.record['events'],before)
        self.assertNotIn('approval',self.record['process_control']['activities'][0])

class TargetIntegration(unittest.TestCase):
    @staticmethod
    def git(root,*args):
        return subprocess.run(['git','-C',str(root),*args],capture_output=True,text=True,check=True).stdout.strip()

    def test_actual_git_new_project_scaffold_and_web_model(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            self.git(root,'init','-q')
            self.git(root,'config','user.email','demo@example.com')
            self.git(root,'config','user.name','Demo')
            self.git(root,'remote','add','origin',REPO)
            (root/'README.md').write_text('existing readme\n',encoding='utf8')
            self.git(root,'add','README.md');self.git(root,'commit','-qm','baseline')
            state=inspect_target(root, REPO)
            prev=create_preview(state,'adopt',project_id='demo')
            payload=json.loads(prev['files']['.project-control/management.json'])
            self.assertEqual(payload['process_control']['profile_id'],'general-rd')
            apply_preview(root,state,prev)
            self.git(root,'add','.project-control','PROJECT_START_HERE.md')
            self.git(root,'commit','-qm','adopt')
            target=Target(root)
            model=target.model()
            self.assertIn('process_catalog',model)
            rev=model['management']['revision']
            target.apply('open_process_activity',{'expected_revision':rev,'reason':'建立候选 P0',
                'id':'p0-1','process_id':'initiation','role':'coordinator',
                'objective':'重新审查立项','source_refs':['用户明确要求']})
            self.assertEqual(len(target.model()['management']['process_control']['activities']),1)
            with self.assertRaises(Exception):
                target.apply('request_process_review',{'expected_revision':rev+1,
                    'reason':'想跳过所有工作','id':'r-1','activity_id':'p0-1','purpose':'批准'})
            self.assertEqual(len(target.model()['management']['process_control']['review_requests']),0)

if __name__=='__main__':unittest.main()
