"""Read-only retained-journal evidence for aged-out builder EC2 instances.

AWS stops returning a terminated instance some time after termination: an
exact-ID DescribeInstances then answers ``{"Reservations": []}`` without an
error, while the tagging index can keep listing it. Such a hit is resolved only
when a builder run's own retained journal proves that run CREATED the exact
instance and OBSERVED it terminated. Every read here is bounded and never fails
open: an unavailable, expired, oversized or malformed journal is simply no
evidence, so the audit keeps the hit as a leftover.
"""
import datetime
import io
import json
import os
import re
import subprocess
import time
import uuid
import zipfile
from pathlib import Path

import builder_journal as safety
import builder_namespace as names

REPOSITORY='MCMike0399/bomberman-64-multiplayer'
WORKFLOW='gpu-qualification.yml'
ARTIFACT=re.compile(r'builder-(?:state-[0-9]+-[0-9]+-final|stopped-[0-9]+-[0-9]+|armed-[0-9]+-[0-9]+)')
JOURNAL_FILE='creation-manifest.jsonl'
RECEIPT_FILE='cleanup-receipt.json'
MAX_RUNS=30
MAX_ARTIFACTS=90
MAX_ARTIFACT_BYTES=20*1024*1024
MAX_MEMBER_BYTES=8*1024*1024
CALL_TIMEOUT_SECONDS=30
TOTAL_BUDGET_SECONDS=150
SHAPE='describe-instances: empty Reservations, no error'
INSTANCE='AWS::EC2::Instance'


def parse_journal(text):
    """Strict parse of one retained journal: pinned account/region, one builder run."""
    rows=[]
    for line in text.splitlines():
        if not line.strip():continue
        row=json.loads(line)
        required={'schemaVersion','eventId','timestampUtc','event','account','region','runId','type','id','parent','dependencies','details'}
        if not isinstance(row,dict)or not required<=row.keys()or row['schemaVersion']!=1 or row['event']not in safety.EVENTS:
            raise ValueError('Malformed retained journal row')
        uuid.UUID(row['eventId']);moment(row['timestampUtc'])
        if (row['account'],row['region'])!=(names.ACCOUNT,names.REGION)or not isinstance(row['details'],dict):
            raise ValueError('Retained journal outside the pinned account/region')
        rows.append(row)
    runs={row['runId']for row in rows}
    if len(runs)!=1 or not re.fullmatch(names.RUN_PATTERN,next(iter(runs))):raise ValueError('Retained journal is not one builder run')
    return rows


def moment(value):
    parsed=datetime.datetime.fromisoformat(value)
    if parsed.tzinfo is None:raise ValueError('Journal timestamp lacks a timezone')
    return parsed


def termination_evidence(instance,rows,receipt=None):
    """Exact CREATED row plus an observed termination by the same run, else None."""
    created=[row for row in rows if row['event']=='CREATED'and row['type']==INSTANCE and row['id']==instance]
    if len(created)!=1:return None
    created=created[0];run=created['runId'];born=moment(created['timestampUtc'])
    status=[row for row in rows if row['event']=='STATUS'and row['type']==INSTANCE and row['id']==instance
            and moment(row['timestampUtc'])>=born]
    observed=[]
    for row in status:
        details=row['details']
        if details.get('state')=='terminated'or details.get('terminatedObservedAtUtc'):
            observed.append({'source':'journal STATUS','eventId':row['eventId'],'timestampUtc':row['timestampUtc'],
                             'state':'terminated','terminatedObservedAtUtc':details.get('terminatedObservedAtUtc')})
    # A STATUS terminationRequestedAtUtc row is appended only after TerminateInstances returned.
    for request in (row for row in status if row['details'].get('terminationRequestedAtUtc')):
        after=[row for row in status if row['details'].get('state')in ('shutting-down','terminated')
               and moment(row['timestampUtc'])>=moment(request['timestampUtc'])]
        if after:
            observed.append({'source':'successful terminate request then observed state','requestEventId':request['eventId'],
                             'requestedAtUtc':request['details']['terminationRequestedAtUtc'],'observedEventId':after[0]['eventId'],
                             'observedAtUtc':after[0]['timestampUtc'],'state':after[0]['details']['state']})
    if isinstance(receipt,dict)and receipt.get('runId')==run and receipt.get('account')==names.ACCOUNT and receipt.get('region')==names.REGION:
        for item in receipt.get('resources')or[]:
            if isinstance(item,dict)and item.get('type')==INSTANCE and item.get('id')==instance and item.get('finalState')in ('terminated','absent'):
                observed.append({'source':'cleanup-receipt resources','finalState':item['finalState'],'receiptObservedAtUtc':receipt.get('observedAtUtc')})
    if not observed:return None
    return {'builderRunId':run,'created':{'eventId':created['eventId'],'timestampUtc':created['timestampUtc'],
            'logicalId':created['details'].get('logicalId')},'observedTermination':observed}


class Journals:
    """Ordered retained-journal sources. Each source yields journal records:
    {'reference':{...}, 'journal':text-or-callable, 'receipt':text-or-None}."""
    def __init__(self,*sources):self.sources=[source for source in sources if source is not None];self.cache={}

    def instance(self,instance):
        if instance in self.cache:return self.cache[instance]
        matches=[];errors=[]
        for source in self.sources:
            try:records=source.records()
            except Exception as error:errors.append({'source':getattr(source,'name','journal source'),'error':str(error)[:300]});continue
            errors.extend(getattr(source,'errors',[]))
            for record in records:
                try:
                    rows=parse_journal(record['journal']);receipt=json.loads(record['receipt'])if record.get('receipt')else None
                    evidence=termination_evidence(instance,rows,receipt)
                except Exception as error:
                    errors.append({'reference':record.get('reference'),'error':'unreadable retained journal: '+str(error)[:200]});continue
                if evidence:matches.append({**evidence,'journal':record['reference']})
        runs={match['builderRunId']for match in matches}
        # One builder run must own it; two different owners would be contradictory.
        result={'evidence':matches[0]if len(runs)==1 else None,'matchingJournals':len(matches),'errors':errors[:20]}
        if len(runs)>1:result['errors'].append({'error':'instance claimed by several builder runs: '+','.join(sorted(runs))})
        self.cache[instance]=result
        return result


class LocalJournal:
    """This run's own state directory, read during its final sweep."""
    name='local state directory'
    def __init__(self,root,reference):self.root=Path(root);self.reference=reference;self.errors=[]

    def records(self):
        journal=self.root/JOURNAL_FILE;receipt=self.root/RECEIPT_FILE
        if not journal.is_file():return []
        return [{'reference':{**self.reference,'path':str(journal)},'journal':journal.read_text(),
                 'receipt':receipt.read_text()if receipt.is_file()else None}]


def gh_api(path,binary=False,timeout=CALL_TIMEOUT_SECONDS):
    """Read-only GitHub REST GET through the job token. Raises on any failure."""
    result=subprocess.run(['gh','api','-X','GET',path],capture_output=True,timeout=timeout,
                          env={**os.environ,'GH_PROMPT_DISABLED':'1'})
    if result.returncode:raise RuntimeError('gh api GET failed: '+result.stderr.decode(errors='replace').strip()[:200])
    if len(result.stdout)>MAX_ARTIFACT_BYTES:raise ValueError('GitHub response exceeds the size cap')
    return result.stdout if binary else json.loads(result.stdout)


class GitHubJournals:
    """Retained journals of recent gpu-qualification runs. Loaded once per process."""
    name='GitHub retained artifacts'
    def __init__(self,repository=REPOSITORY,api=gh_api,clock=time.monotonic,max_runs=MAX_RUNS):
        self.repository=repository;self.api=api;self.clock=clock;self.max_runs=max_runs;self.loaded=None;self.errors=[]

    def records(self):
        if self.loaded is None:self.loaded=self.load()
        return self.loaded

    def load(self):
        stop=self.clock()+TOTAL_BUDGET_SECONDS;records=[];base='repos/'+self.repository+'/actions/'
        runs=self.api(base+'workflows/'+WORKFLOW+'/runs?per_page='+str(self.max_runs))['workflow_runs'][:self.max_runs]
        for run in runs:
            if len(records)>=MAX_ARTIFACTS or self.clock()>stop:
                self.errors.append({'error':'retained-journal search budget exhausted'});break
            try:artifacts=self.api(base+'runs/'+str(int(run['id']))+'/artifacts?per_page=100')['artifacts']
            except Exception as error:self.errors.append({'workflowRunId':run.get('id'),'error':str(error)[:200]});continue
            for artifact in artifacts:
                if not ARTIFACT.fullmatch(str(artifact.get('name','')))or artifact.get('expired'):continue
                reference={'source':'GitHub artifact','workflow':WORKFLOW,'workflowRunId':run['id'],
                           'artifactName':artifact['name'],'artifactId':artifact['id'],'artifactDigest':artifact.get('digest')}
                try:
                    if not 0<int(artifact.get('size_in_bytes',0))<=MAX_ARTIFACT_BYTES:raise ValueError('artifact size outside the cap')
                    if self.clock()>stop:raise TimeoutError('retained-journal search budget exhausted')
                    data=self.api(base+'artifacts/'+str(int(artifact['id']))+'/zip',binary=True)
                    records.extend(unzip(data,reference))
                except Exception as error:self.errors.append({'reference':reference,'error':str(error)[:200]})
        return records


def unzip(data,reference):
    with zipfile.ZipFile(io.BytesIO(data))as archive:
        members={info.filename:info for info in archive.infolist()}
        def read(name):
            info=members.get(name)
            if info is None:return None
            if info.file_size>MAX_MEMBER_BYTES:raise ValueError(name+' exceeds the size cap')
            return archive.read(info).decode()
        journal=read(JOURNAL_FILE)
        return [] if journal is None else [{'reference':reference,'journal':journal,'receipt':read(RECEIPT_FILE)}]


_DEFAULT=None


def default():
    """GitHub source only with an explicit job token for this repository; cached per process."""
    global _DEFAULT
    if _DEFAULT is None:
        token=os.environ.get('GH_TOKEN')or os.environ.get('GITHUB_TOKEN')
        repository=os.environ.get('GITHUB_REPOSITORY',REPOSITORY)
        _DEFAULT=Journals(GitHubJournals())if token and repository==REPOSITORY else Journals(Unavailable('GH_TOKEN unavailable'))
    return _DEFAULT


class Unavailable:
    name='unavailable'
    def __init__(self,reason):self.reason=reason;self.errors=[]
    def records(self):raise RuntimeError(self.reason)
